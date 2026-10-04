using System.Globalization;

namespace VipShop;

/// <summary>Package durations are whole hours: shown as "3 days", "12 hours", "1 day 6 hours".</summary>
public static class Duration
{
    public static string Text(int hours)
    {
        if (hours <= 0) return "0 hours";
        int d = hours / 24, h = hours % 24;
        string days = d == 1 ? "1 day" : d + " days";
        string hrs = h == 1 ? "1 hour" : h + " hours";
        return d == 0 ? hrs : h == 0 ? days : days + " " + hrs;
    }

    /// <summary>Time from now to the end of a package: "2 days 3 hours", "45 minutes"; null when it is over.</summary>
    public static string? Left(DateTime end, DateTime now)
    {
        var left = end - now;
        if (left <= TimeSpan.Zero) return null;
        if (left.TotalHours >= 1) return Text((int)Math.Min(int.MaxValue, Math.Floor(left.TotalHours)));
        int m = Math.Max(1, (int)Math.Floor(left.TotalMinutes));
        return m == 1 ? "1 minute" : m + " minutes";
    }

    public static string Date(DateTime d) => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Limits of a package (admin form and API). The save procedure checks them again.</summary>
public static class PackageRules
{
    public const int MaxName = 50, MaxDescription = 200, MaxPrice = 1_000_000_000, MaxDays = 3650, MaxHours = MaxDays * 24;

    public static List<string> Validate(VipPackage p)
    {
        var errors = new List<string>();
        if (p.PackageId < 0) errors.Add("package_id must be 0 (new) or an existing package");
        if (p.Name.Trim().Length == 0) errors.Add("Name is required");
        if (p.Name.Length > MaxName) errors.Add($"Name: at most {MaxName} characters");
        if (p.Description.Length > MaxDescription) errors.Add($"Description: at most {MaxDescription} characters");
        if (p.Price < 0 || p.Price > MaxPrice) errors.Add($"Price must be 0 to {MaxPrice:#,0}");
        if (p.DurationHours < 1 || p.DurationHours > MaxHours)
            errors.Add($"Duration must be at least 1 hour and at most {MaxDays} days");
        var main = p.Main;
        for (int i = 0; i < main.Length; i++)
            if (main[i] < 0 || main[i] > VipPackage.MaxPct) errors.Add($"{VipPackage.MainLabels[i]} % must be 0 to {VipPackage.MaxPct}");
        var pct = p.StatPct;
        var add = p.StatAdd;
        for (int i = 0; i < 12; i++)
        {
            if (pct[i] < 0 || pct[i] > VipPackage.MaxPct) errors.Add($"{VipPackage.Stats[i]} % must be 0 to {VipPackage.MaxPct}");
            if (add[i] < 0 || add[i] > VipPackage.MaxAdd) errors.Add($"{VipPackage.Stats[i]} + must be 0 to {VipPackage.MaxAdd:#,0}");
        }
        return errors;
    }

    /// <summary>
    /// A package from the admin form: field names = column names (package_id, name, description, price, sort, enabled,
    /// exp_pct ... hv_pct, ap_add ... hv_add) and the duration as days + hours. Empty number fields count as 0.
    /// Returns the package (with the values that could be read) and every problem found, empty = valid.
    /// </summary>
    public static (VipPackage Package, List<string> Errors) FromForm(Func<string, string?> field)
    {
        var errors = new List<string>();
        int Num(string name, string label)
        {
            var s = field(name)?.Trim();
            if (string.IsNullOrEmpty(s)) return 0;
            if (int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)) return v;
            errors.Add(label + ": not a whole number");
            return 0;
        }
        int days = Num("days", "Days"), hours = Num("hours", "Hours");
        if (days < 0 || days > MaxDays) errors.Add($"Days must be 0 to {MaxDays}");
        if (hours < 0 || hours > 23) errors.Add("Hours must be 0 to 23 (use days for more)");
        var p = new VipPackage
        {
            PackageId = Num("package_id", "package_id"),
            Name = (field("name") ?? "").Trim(),
            Description = (field("description") ?? "").Trim(),
            Price = Num("price", "Price"),
            DurationHours = (int)Math.Clamp((long)days * 24 + hours, int.MinValue, int.MaxValue),
            Sort = Num("sort", "Sort"),
            Enabled = field("enabled") is "on" or "1" or "true",
        }.WithEffects(
            VipPackage.MainColumns.Select((col, i) => Num(col, VipPackage.MainLabels[i] + " %")).ToArray(),
            Enumerable.Range(0, 12).Select(i => Num(VipPackage.PctColumn(i), VipPackage.Stats[i] + " %")).ToArray(),
            Enumerable.Range(0, 12).Select(i => Num(VipPackage.AddColumn(i), VipPackage.Stats[i] + " +")).ToArray());
        errors.AddRange(Validate(p));
        return (p, errors.Distinct().ToList());
    }
}

public enum BuyKind
{
    /// <summary>The character has no active package: it starts now.</summary>
    New,
    /// <summary>Same package still active: the end time moves later by the package duration.</summary>
    Extend,
    /// <summary>Another package is active: it ends now and the new one starts (its remaining time is lost).</summary>
    Replace,
}

/// <summary>What a purchase will do, shown on the confirm page. The end time is an estimate; uspw_vip_buy sets the real one.</summary>
public sealed record BuyPlan(BuyKind Kind, int Price, int Cash, DateTime EndsAbout)
{
    public bool Enough => Cash >= Price;
    public int CashAfter => Cash - Price;

    public static BuyPlan Of(VipChar c, VipPackage p, int cash, DateTime now)
    {
        if (c.IsActive(now) && c.PackageId == p.PackageId)
            return new(BuyKind.Extend, p.Price, cash, c.EndDate!.Value.AddHours(p.DurationHours));
        return new(c.IsActive(now) ? BuyKind.Replace : BuyKind.New, p.Price, cash, now.AddHours(p.DurationHours));
    }
}

/// <summary>RETURN codes of uspw_vip_buy.</summary>
public static class BuyCodes
{
    public const int Ok = 0, NotEnoughPoints = 1, NotYourCharacter = 2, PackageUnavailable = 3, PriceChanged = 4, Failed = 5;

    public static string Message(int code, string pointName) => code switch
    {
        Ok => "Purchase complete.",
        NotEnoughPoints => $"Not enough {pointName}.",
        NotYourCharacter => "This character is not on your account (or was deleted).",
        PackageUnavailable => "This package is no longer on sale.",
        PriceChanged => "The price of this package has changed. Check the new price and buy again.",
        _ => "The purchase failed (nothing was bought). Please try again later.",
    };

    /// <summary>Short machine name of a code (API field "error").</summary>
    public static string Name(int code) => code switch
    {
        Ok => "ok",
        NotEnoughPoints => "not_enough_points",
        NotYourCharacter => "character_not_owned",
        PackageUnavailable => "package_unavailable",
        PriceChanged => "price_changed",
        _ => "failed",
    };
}
