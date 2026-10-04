using Microsoft.Extensions.Time.Testing;

namespace VipShop.Tests;

public class LoginKeyTests
{
    [Fact]
    public void MatchesTheStoredAccountPw()
    {
        // TrickMod docs/login: krskilltest1 / 12345678 -> tbl_account.account_pw 83aa400af464c76d
        Assert.Equal("83aa400af464c76d", LoginKey.Hex("12345678"));
        // the launcher passes it as a signed decimal (top bit set = negative), uspw_cash_auth converts it back
        Assert.Equal("-8959278092906084499", LoginKey.Of("12345678"));                // README example
        Assert.StartsWith("-", LoginKey.Of("12345678"));
    }

    [Fact]
    public void SameRulesAsFnMd5Encrypt()
    {
        Assert.Equal(LoginKey.Hex("abcDEF12"), LoginKey.Hex("ABCdef12"));             // LOWER
        Assert.Equal(LoginKey.Hex("12345678"), LoginKey.Hex("12345678   "));           // RTRIM
        Assert.NotEqual(LoginKey.Hex("12345678"), LoginKey.Hex(" 12345678"));          // no LTRIM
        Assert.Equal(LoginKey.Hex("123456789012345678"), LoginKey.Hex("123456789012345678xyz"));  // varchar(18)
        Assert.Null(LoginKey.Of(""));
        Assert.Null(LoginKey.Of("   "));
        Assert.Null(LoginKey.Of("pässword"));                                          // not ASCII 32..126
    }
}

public class DurationTests
{
    [Theory]
    [InlineData(72, "3 days")]
    [InlineData(12, "12 hours")]
    [InlineData(30, "1 day 6 hours")]
    [InlineData(1, "1 hour")]
    [InlineData(24, "1 day")]
    [InlineData(49, "2 days 1 hour")]
    [InlineData(720, "30 days")]
    public void Text(int hours, string text) => Assert.Equal(text, Duration.Text(hours));

    [Fact]
    public void Left()
    {
        var now = new DateTime(2026, 10, 5, 12, 0, 0);
        Assert.Equal("1 hour", Duration.Left(now.AddMinutes(90), now));
        Assert.Equal("45 minutes", Duration.Left(now.AddMinutes(45), now));
        Assert.Equal("3 days 5 hours", Duration.Left(now.AddHours(77).AddMinutes(30), now));
        Assert.Null(Duration.Left(now, now));
        Assert.Null(Duration.Left(now.AddSeconds(-1), now));
        Assert.Equal("2026-10-08 17:00", Duration.Date(now.AddHours(77)));
    }
}

public class PackageRulesTests
{
    private static Func<string, string?> Fields(params (string, string)[] f)
    {
        var d = new Dictionary<string, string>
        {
            ["package_id"] = "0", ["name"] = "Gold", ["description"] = "", ["price"] = "1000", ["days"] = "30", ["hours"] = "0",
            ["sort"] = "1", ["enabled"] = "1", ["exp_pct"] = "50", ["ap_pct"] = "10", ["ap_add"] = "50",
        };
        foreach (var (k, v) in f) d[k] = v;
        return k => d.GetValueOrDefault(k);
    }

    [Fact]
    public void ValidFormGivesThePackage()
    {
        var (p, errors) = PackageRules.FromForm(Fields(("days", "1"), ("hours", "6"), ("hv_add", "10000"), ("find_pct", "100")));
        Assert.Empty(errors);
        Assert.Equal(30, p.DurationHours);
        Assert.Equal(50, p.ExpPct);
        Assert.Equal(10, p.ApPct);
        Assert.Equal(50, p.ApAdd);
        Assert.Equal(10000, p.HvAdd);
        Assert.Equal(100, p.FindPct);
        Assert.True(p.Enabled);
        Assert.False(PackageRules.FromForm(Fields(("enabled", ""))).Package.Enabled);
    }

    [Theory]
    [InlineData("exp_pct", "101", "EXP % must be 0 to 100")]
    [InlineData("speed_pct", "-1", "Run speed % must be 0 to 100")]
    [InlineData("hp_pct", "150", "HP % must be 0 to 100")]
    [InlineData("dp_add", "10001", "DP + must be 0 to 10,000")]
    [InlineData("price", "-5", "Price must be 0 to 1,000,000,000")]
    [InlineData("price", "12.5", "Price: not a whole number")]
    [InlineData("name", " ", "Name is required")]
    [InlineData("hours", "24", "Hours must be 0 to 23 (use days for more)")]
    [InlineData("days", "3651", "Days must be 0 to 3650")]
    public void RefusesOutOfRange(string field, string value, string error)
    {
        var (_, errors) = PackageRules.FromForm(Fields((field, value)));
        Assert.Contains(error, errors);
    }

    [Fact]
    public void DurationMustBePositive()
    {
        Assert.Contains("Duration must be at least 1 hour and at most 3650 days",
            PackageRules.FromForm(Fields(("days", "0"), ("hours", "0"))).Errors);
        Assert.Empty(PackageRules.FromForm(Fields(("days", "0"), ("hours", "1"))).Errors);
        Assert.Empty(PackageRules.FromForm(Fields(("days", "3650"), ("hours", "0"))).Errors);  // 87600 h = the SQL limit
        Assert.NotEmpty(PackageRules.Validate(new VipPackage { Name = "x", DurationHours = 87601 }));
        Assert.Empty(PackageRules.Validate(new VipPackage { Name = "x", DurationHours = 1, Price = 0 }));  // free is allowed
    }
}

public class BuyPlanTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0);
    private static readonly VipPackage Gold = new() { PackageId = 2, Name = "Gold", Price = 10000, DurationHours = 720 };

    [Fact]
    public void NoPackageStartsNow()
    {
        var plan = BuyPlan.Of(new VipChar(1, "a", 1, 1, null, null, null), Gold, 25000, Now);
        Assert.Equal(BuyKind.New, plan.Kind);
        Assert.Equal(Now.AddHours(720), plan.EndsAbout);
        Assert.True(plan.Enough);
        Assert.Equal(15000, plan.CashAfter);
    }

    [Fact]
    public void SameRunningPackageExtendsFromItsEnd()
    {
        var end = Now.AddHours(77);
        var plan = BuyPlan.Of(new VipChar(1, "a", 1, 1, 2, "Gold", end), Gold, 10000, Now);
        Assert.Equal(BuyKind.Extend, plan.Kind);
        Assert.Equal(end.AddHours(720), plan.EndsAbout);
        Assert.True(plan.Enough);          // exactly enough
        Assert.Equal(0, plan.CashAfter);
    }

    [Fact]
    public void OtherRunningPackageIsReplaced()
    {
        var plan = BuyPlan.Of(new VipChar(1, "a", 1, 1, 1, "Silver", Now.AddHours(5)), Gold, 9999, Now);
        Assert.Equal(BuyKind.Replace, plan.Kind);
        Assert.Equal(Now.AddHours(720), plan.EndsAbout);
        Assert.False(plan.Enough);
    }

    [Fact]
    public void EndedPackageCountsAsNone()
    {
        var plan = BuyPlan.Of(new VipChar(1, "a", 1, 1, 2, "Gold", Now), Gold, 0, Now);
        Assert.Equal(BuyKind.New, plan.Kind);
        Assert.False(plan.Enough);
    }

    [Fact]
    public void EveryBuyCodeHasItsOwnMessage()
    {
        var codes = new[] { 0, 1, 2, 3, 4, 5 };
        Assert.Equal(codes.Length, codes.Select(c => BuyCodes.Message(c, "Cash")).Distinct().Count());
        Assert.Equal(codes.Length, codes.Select(BuyCodes.Name).Distinct().Count());
        Assert.Equal("failed", BuyCodes.Name(99));
    }
}

public class TokenTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void PlayerSessionRoundTrip()
    {
        var t = new Tokens("s");
        var token = t.IssuePlayer(new PlayerSession(1001, "odd|name"), Hour);
        Assert.Equal(new PlayerSession(1001, "odd|name"), t.Player(token));
        Assert.False(t.Admin(token));
        Assert.Null(new Tokens("other").Player(token));
        Assert.Null(t.Player(token[..^2] + (token[^2] == 'A' ? "BB" : "AA")));
        Assert.Null(t.Player("garbage"));
        Assert.Null(t.Player(null));
    }

    [Fact]
    public void Expire()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var t = new Tokens("s", time);
        var player = t.IssuePlayer(new PlayerSession(1, "a"), Hour);
        var admin = t.IssueAdmin(Hour);
        Assert.True(t.Admin(admin));
        Assert.Null(t.Player(admin));
        time.Advance(TimeSpan.FromMinutes(61));
        Assert.Null(t.Player(player));
        Assert.False(t.Admin(admin));
    }

    [Fact]
    public void BuyTicketWorksOnceForItsAccountOnly()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var t = new Tokens("s", time);
        var ticket = new BuyTicket(1001, 5001, 2, 10000);
        var token = t.IssueBuy(ticket, Hour);
        Assert.Null(t.RedeemBuy(token, 1002));
        Assert.Equal(ticket, t.RedeemBuy(token, 1001));
        Assert.Null(t.RedeemBuy(token, 1001));
        Assert.Null(t.Player(token));

        var late = t.IssueBuy(ticket, Hour);
        time.Advance(TimeSpan.FromMinutes(61));
        Assert.Null(t.RedeemBuy(late, 1001));
    }
}
