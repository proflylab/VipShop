using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace VipShop.Tests;

/// <summary>
/// In-memory stand-in for the gmg_account procedures with the rules of TrickMod sql/vip/10_vip.sql (uspw_vip_buy:
/// same running package = extend from its end, another running package = replace now, none = now + duration;
/// return codes 0..4). Lets every page and API call run without a database.
/// </summary>
internal sealed class FakeDb(TimeProvider time) : IVipDb
{
    public const string Gid = "tester", Password = "12345678";
    public const int Code = 1001, OtherCode = 1002;

    public readonly Dictionary<int, Account> Accounts = new()
    {
        [Code] = new Account(Code, Gid, 25000),
        [OtherCode] = new Account(OtherCode, "someone", 99999),
    };
    public readonly Dictionary<int, (int Account, string Name, int Level, int Job)> Chars = new()
    {
        [5001] = (Code, "Bunny", 85, 10),
        [5002] = (Code, "Sheepy", 40, 3),
        [5003] = (Code, "Foxy", 120, 14),
        [6001] = (OtherCode, "NotYours", 50, 2),
    };
    public readonly Dictionary<int, (int Package, DateTime End)> Vip = [];
    public readonly List<VipPackage> Packages =
    [
        new VipPackage { PackageId = 1, Name = "Silver 7 days", Description = "EXP / TM boost for a week.", Price = 3000,
            DurationHours = 168, ExpPct = 20, TmPct = 20, SpeedPct = 10, FindPct = 10, Sort = 10 },
        new VipPackage { PackageId = 2, Name = "Gold 30 days", Description = "Everything of Silver, more, and stats.", Price = 10000,
            DurationHours = 720, ExpPct = 50, TmPct = 50, SpeedPct = 20, FindPct = 20, ApPct = 10, ApAdd = 50, MaPct = 10,
            HpPct = 5, HpAdd = 500, LkAdd = 30, Sort = 20 },
        new VipPackage { PackageId = 3, Name = "Trial", Description = "", Price = 500, DurationHours = 30, ExpPct = 10, Sort = 30 },
        new VipPackage { PackageId = 4, Name = "Old event", Price = 1, DurationHours = 24, ExpPct = 100, Enabled = false, Sort = 40 },
    ];
    public readonly List<VipLog> Logs = [];
    public int BuyCalls;

    private DateTime Now => time.GetLocalNow().DateTime;

    public FakeDb Seed()
    {
        Vip[5001] = (2, Now.AddHours(77));          // Gold, 3 days 5 hours left
        Vip[5003] = (1, Now.AddDays(-2));           // Silver, ended
        Logs.Add(new VipLog(1, Now.AddDays(-40), Code, Gid, 5003, "Foxy", 1, "Silver 7 days", 3000, 168, "new", null,
            Now.AddDays(-33), 40000));
        Logs.Add(new VipLog(2, Now.AddDays(-27), Code, Gid, 5001, "Bunny", 2, "Gold 30 days", 10000, 720, "new", null,
            Now.AddDays(3), 30000));
        Logs.Add(new VipLog(3, Now.AddDays(-9), Code, Gid, 5003, "Foxy", 1, "Silver 7 days", 3000, 168, "replace", 3,
            Now.AddDays(-2), 25000));
        return this;
    }

    public Task<int> AuthAsync(string accountGid, string loginKey) =>
        Task.FromResult(accountGid == Gid && loginKey == LoginKey.Of(Password) ? Code : 0);

    public Task<Account?> AccountAsync(int accountCode) => Task.FromResult(Accounts.GetValueOrDefault(accountCode));

    public Task<List<VipPackage>> PackagesAsync(bool all) =>
        Task.FromResult(Packages.Where(p => all || p.Enabled).OrderBy(p => p.Sort).ThenBy(p => p.PackageId).ToList());

    public Task<(int Code, int PackageId)> SavePackageAsync(VipPackage p)
    {
        if (PackageRules.Validate(p).Count > 0) return Task.FromResult((1, p.PackageId));
        p = p with { UpdateDate = Now };
        if (p.PackageId == 0)
        {
            p = p with { PackageId = Packages.Max(x => x.PackageId) + 1 };
            Packages.Add(p);
            return Task.FromResult((0, p.PackageId));
        }
        int i = Packages.FindIndex(x => x.PackageId == p.PackageId);
        if (i < 0) return Task.FromResult((2, p.PackageId));
        Packages[i] = p;
        return Task.FromResult((0, p.PackageId));
    }

    public Task<DeleteResult> DeletePackageAsync(int packageId)
    {
        int i = Packages.FindIndex(x => x.PackageId == packageId);
        if (i < 0) return Task.FromResult(DeleteResult.NotFound);
        if (Logs.Any(l => l.PackageId == packageId) || Vip.Values.Any(v => v.Package == packageId))
        {
            Packages[i] = Packages[i] with { Enabled = false };
            return Task.FromResult(DeleteResult.Disabled);
        }
        Packages.RemoveAt(i);
        return Task.FromResult(DeleteResult.Deleted);
    }

    public Task<List<VipChar>> CharactersAsync(int accountCode) => Task.FromResult(Chars
        .Where(c => c.Value.Account == accountCode)
        .Select(c =>
        {
            bool active = Vip.TryGetValue(c.Key, out var v) && v.End > Now;
            return new VipChar(c.Key, c.Value.Name, c.Value.Level, c.Value.Job, active ? v.Package : null,
                active ? Packages.First(p => p.PackageId == v.Package).Name : null, active ? v.End : null);
        }).ToList());

    public Task<BuyResult> BuyAsync(int accountCode, int charUid, int packageId, int expectedPrice)
    {
        BuyCalls++;
        var a = Accounts[accountCode];
        if (!Chars.TryGetValue(charUid, out var c) || c.Account != accountCode) return Fail(BuyCodes.NotYourCharacter);
        var p = Packages.FirstOrDefault(x => x.PackageId == packageId && x.Enabled);
        if (p is null) return Fail(BuyCodes.PackageUnavailable);
        if (p.Price != expectedPrice) return Fail(BuyCodes.PriceChanged);
        if (a.Cash < p.Price) return Fail(BuyCodes.NotEnoughPoints);
        bool running = Vip.TryGetValue(charUid, out var old) && old.End > Now;
        string kind = running && old.Package == packageId ? "extend" : running ? "replace" : "new";
        var end = (kind == "extend" ? old.End : Now).AddHours(p.DurationHours);
        Vip[charUid] = (packageId, end);
        Accounts[accountCode] = a = a with { Cash = a.Cash - p.Price };
        Logs.Add(new VipLog(Logs.Count + 1, Now, accountCode, a.AccountGid, charUid, c.Name, p.PackageId, p.Name, p.Price,
            p.DurationHours, kind, kind == "replace" ? old.Package : null, end, a.Cash));
        return Task.FromResult(new BuyResult(BuyCodes.Ok, a.Cash, end));

        Task<BuyResult> Fail(int code) => Task.FromResult(new BuyResult(code, a.Cash, null));
    }

    public Task<List<VipLog>> HistoryAsync(int accountCode, int rows) => Task.FromResult(Logs
        .Where(l => l.AccountCode == accountCode).OrderByDescending(l => l.LogId).Take(rows)
        .Select(l => l with { AccountCode = null, AccountGid = null }).ToList());

    public Task<List<VipLog>> LogAsync(int rows) => Task.FromResult(Logs.OrderByDescending(l => l.LogId).Take(rows).ToList());
}

/// <summary>The real site (Program.cs, every page and API route) on a test server with <see cref="FakeDb"/> and a fixed clock.</summary>
internal sealed class Site : IDisposable
{
    public readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
    public readonly FakeDb Db;
    private readonly WebApplicationFactory<Program> _factory;

    public Site(string adminPassword = "admin-pw")
    {
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Db = new FakeDb(Time).Seed();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Account", "Server=none");
            b.UseSetting("Admin:Password", adminPassword);
            b.UseSetting("Vip:Secret", "test-secret");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IVipDb>(Db);
                s.AddSingleton<TimeProvider>(Time);
            });
        });
    }

    public HttpClient Client(bool redirects = true) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = redirects });

    public async Task<HttpClient> Player()
    {
        var c = Client();
        var r = await c.PostAsync("/login", Form(("account", FakeDb.Gid), ("password", FakeDb.Password)));
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        return c;
    }

    public async Task<HttpClient> Admin()
    {
        var c = Client();
        var r = await c.PostAsync("/admin/login", Form(("password", "admin-pw")));
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        return c;
    }

    public static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    public static string Token(string html) => Regex.Match(html, "name=\"token\" value=\"([^\"]+)\"").Groups[1].Value;

    public void Dispose() => _factory.Dispose();
}

/// <summary>With VIPSHOP_PREVIEW=&lt;dir&gt; the tests also write the pages there (with assets/site.css) to look at them.</summary>
internal static class Preview
{
    public static string Save(string name, string html)
    {
        var dir = Environment.GetEnvironmentVariable("VIPSHOP_PREVIEW");
        if (string.IsNullOrEmpty(dir)) return html;
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "site.css"), Path.Combine(dir, "assets", "site.css"), true);
        File.WriteAllText(Path.Combine(dir, name + ".html"), html.Replace("\"/assets/", "\"assets/"));
        return html;
    }
}
