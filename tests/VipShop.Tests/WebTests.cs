using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static VipShop.Tests.Site;

namespace VipShop.Tests;

/// <summary>Every page through the real routes with the fake database. VIPSHOP_PREVIEW=&lt;dir&gt; also saves the pages.</summary>
public class PlayerPageTests
{
    [Fact]
    public async Task LoginPageWithoutSession()
    {
        using var site = new Site();
        var html = Preview.Save("login", await site.Client().GetStringAsync("/"));
        Assert.Contains("action=\"/login\"", html);
        Assert.Contains("name=\"password\"", html);
        var css = await site.Client().GetAsync("/assets/site.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
    }

    [Fact]
    public async Task WrongPasswordIsRefused()
    {
        using var site = new Site();
        var c = site.Client();
        var r = await c.PostAsync("/login", Form(("account", FakeDb.Gid), ("password", "nope")));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("Wrong account or password.", Preview.Save("login_error", await r.Content.ReadAsStringAsync()));
        Assert.Contains("action=\"/login\"", await c.GetStringAsync("/"));
    }

    [Fact]
    public async Task HomeShowsPointsCharactersAndPackages()
    {
        using var site = new Site();
        var html = Preview.Save("home", await (await site.Player()).GetStringAsync("/"));
        Assert.Contains("25,000", html);
        Assert.Contains("Bunny", html);
        Assert.Contains("Gold 30 days", html);
        Assert.Contains("until 2026-10-08 17:00 (3 days 5 hours left)", html);
        Assert.Contains("1 day 6 hours", html);                // Trial: 30 hours
        Assert.Contains("<b>AP</b> +10% +50", html);
        Assert.Contains("<b>LK</b> +30", html);
        Assert.DoesNotContain("Old event", html);              // disabled
        Assert.DoesNotContain("NotYours", html);
        Assert.Contains("action=\"/buy\"", html);
    }

    [Fact]
    public async Task PackagesArePublic()
    {
        using var site = new Site();
        var html = Preview.Save("packages", await site.Client().GetStringAsync("/packages"));
        Assert.Contains("Silver 7 days", html);
        Assert.Contains("Log in to buy", html);
        Assert.Contains("+20%</b> EXP", html);
    }

    [Fact]
    public async Task ConfirmWarnsBeforeReplacingAnotherPackage()
    {
        using var site = new Site();
        var html = Preview.Save("confirm_replace", await (await site.Player()).GetStringAsync("/buy?char=5001&package=1"));
        Assert.Contains("This replaces the current package", html);
        Assert.Contains("name=\"replace\" value=\"1\" required", html);
        Assert.Contains("ends at about 2026-10-12 12:00", html);
        Assert.Contains("22,000", html);
        Assert.NotEmpty(Token(html));
    }

    [Fact]
    public async Task ConfirmOfTheSamePackageExtends()
    {
        using var site = new Site();
        var html = Preview.Save("confirm_extend", await (await site.Player()).GetStringAsync("/buy?char=5001&package=2"));
        Assert.Contains("already has Gold 30 days until 2026-10-08 17:00", html);
        Assert.Contains("end at about 2026-11-07 17:00", html);
        Assert.DoesNotContain("name=\"replace\"", html);
    }

    [Fact]
    public async Task ConfirmOfANewPackage()
    {
        using var site = new Site();
        var html = await (await site.Player()).GetStringAsync("/buy?char=5003&package=3");   // Foxy's Silver has ended
        Assert.Contains("starts now and ends at about 2026-10-06 18:00", html);
    }

    [Fact]
    public async Task NotEnoughPointsHasNoBuyButton()
    {
        using var site = new Site();
        site.Db.Accounts[FakeDb.Code] = site.Db.Accounts[FakeDb.Code] with { Cash = 100 };
        var html = Preview.Save("confirm_not_enough", await (await site.Player()).GetStringAsync("/buy?char=5002&package=1"));
        Assert.Contains("Not enough Cash: you need 2,900 more.", html);
        Assert.Empty(Token(html));
    }

    [Fact]
    public async Task OtherAccountsCharacterOrDisabledPackageCannotBeBought()
    {
        using var site = new Site();
        var c = await site.Player();
        var r = await c.GetAsync("/buy?char=6001&package=1");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("not on your account", await r.Content.ReadAsStringAsync());
        r = await c.GetAsync("/buy?char=5002&package=4");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("no longer on sale", Preview.Save("cannot_buy", await r.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task BuyUsesTheConfirmPageOnce()
    {
        using var site = new Site();
        var c = await site.Player();
        var token = Token(await c.GetStringAsync("/buy?char=5002&package=1"));
        var r = await c.PostAsync("/buy", Form(("token", token)));
        var html = Preview.Save("result", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("<b>Silver 7 days</b> is active on <b>Sheepy</b> until <b>2026-10-12 12:00</b>", html);
        Assert.Contains("Cash left: 22,000", html);
        Assert.Equal(22000, site.Db.Accounts[FakeDb.Code].Cash);
        Assert.Equal(1, site.Db.BuyCalls);

        r = await c.PostAsync("/buy", Form(("token", token)));        // reload / double click
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("already used", Preview.Save("result_used", await r.Content.ReadAsStringAsync()));
        Assert.Equal(1, site.Db.BuyCalls);
        Assert.Equal(22000, site.Db.Accounts[FakeDb.Code].Cash);
    }

    [Fact]
    public async Task PriceChangedAfterTheConfirmPage()
    {
        using var site = new Site();
        var c = await site.Player();
        var token = Token(await c.GetStringAsync("/buy?char=5002&package=1"));
        site.Db.Packages[0] = site.Db.Packages[0] with { Price = 3500 };
        var r = await c.PostAsync("/buy", Form(("token", token)));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var html = Preview.Save("result_failed", await r.Content.ReadAsStringAsync());
        Assert.Contains("The price of this package has changed", html);
        Assert.Contains("href=\"/buy?char=5002&amp;package=1\"", html);
        Assert.Equal(25000, site.Db.Accounts[FakeDb.Code].Cash);
    }

    [Fact]
    public async Task BuyWithoutLoginOrForeignTokenDoesNothing()
    {
        using var site = new Site();
        var forged = new Tokens("other-secret").IssueBuy(new BuyTicket(FakeDb.Code, 5002, 1, 3000), TimeSpan.FromHours(1));
        var r = await site.Client().PostAsync("/buy", Form(("token", forged)));
        Assert.Contains("action=\"/login\"", await r.Content.ReadAsStringAsync());
        r = await (await site.Player()).PostAsync("/buy", Form(("token", forged)));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, site.Db.BuyCalls);
    }

    [Fact]
    public async Task History()
    {
        using var site = new Site();
        var html = Preview.Save("history", await (await site.Player()).GetStringAsync("/history"));
        Assert.Contains("Purchase history", html);
        Assert.Contains("<span class=\"tag replace\">replace</span>", html);
        Assert.Contains("7 days", html);
    }

    [Fact]
    public async Task LogoutForgetsTheSession()
    {
        using var site = new Site();
        var c = await site.Player();
        await c.PostAsync("/logout", Form());
        Assert.Contains("action=\"/login\"", await c.GetStringAsync("/"));
    }
}

public class AdminPageTests
{
    [Fact]
    public async Task LoginRequired()
    {
        using var site = new Site();
        var c = site.Client();
        Assert.Contains("action=\"/admin/login\"", Preview.Save("admin_login", await c.GetStringAsync("/admin")));
        var r = await c.PostAsync("/admin/login", Form(("password", "wrong")));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        r = await c.PostAsync("/admin/package", Form(("name", "x")));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        // a player login is no admin login
        var player = await site.Player();
        Assert.Contains("action=\"/admin/login\"", await player.GetStringAsync("/admin"));
    }

    [Fact]
    public async Task OffWithoutPassword()
    {
        using var site = new Site(adminPassword: "");
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client().GetAsync("/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client().GetAsync("/api/admin/packages")).StatusCode);
    }

    [Fact]
    public async Task PackageListShowsDisabledToo()
    {
        using var site = new Site();
        var html = Preview.Save("admin_packages", await (await site.Admin()).GetStringAsync("/admin"));
        Assert.Contains("Old event", html);
        Assert.Contains("<span class=\"tag off\">disabled</span>", html);
        Assert.Contains("href=\"/admin/package?id=2\"", html);
    }

    [Fact]
    public async Task EditFormHasEveryField()
    {
        using var site = new Site();
        var html = Preview.Save("admin_edit", await (await site.Admin()).GetStringAsync("/admin/package?id=3"));
        Assert.Contains("name=\"days\" value=\"1\"", html);
        Assert.Contains("name=\"hours\" value=\"6\"", html);
        foreach (var col in VipPackage.MainColumns) Assert.Contains("name=\"" + col + "\"", html);
        for (int i = 0; i < 12; i++)
        {
            Assert.Contains("name=\"" + VipPackage.PctColumn(i) + "\"", html);
            Assert.Contains("name=\"" + VipPackage.AddColumn(i) + "\"", html);
        }
        Assert.Contains("New package", Preview.Save("admin_new", await (await site.Admin()).GetStringAsync("/admin/package")));
    }

    [Fact]
    public async Task InvalidValuesAreShownAndNotSaved()
    {
        using var site = new Site();
        var r = await (await site.Admin()).PostAsync("/admin/package", Form(("package_id", "0"), ("name", "Bad"), ("price", "-1"),
            ("days", "0"), ("hours", "0"), ("exp_pct", "150"), ("ap_add", "20000")));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var html = Preview.Save("admin_edit_errors", await r.Content.ReadAsStringAsync());
        Assert.Contains("EXP % must be 0 to 100", html);
        Assert.Contains("AP + must be 0 to 10,000", html);
        Assert.Contains("Price must be 0 to", html);
        Assert.Contains("Duration must be at least 1 hour", html);
        Assert.Contains("value=\"Bad\"", html);                    // what was typed stays in the form
        Assert.Equal(4, site.Db.Packages.Count);
    }

    [Fact]
    public async Task CreateEditDelete()
    {
        using var site = new Site();
        var c = await site.Admin();
        var r = await c.PostAsync("/admin/package", Form(("package_id", "0"), ("name", "Weekend"), ("description", "Sat+Sun"),
            ("price", "1500"), ("days", "2"), ("hours", "6"), ("sort", "5"), ("enabled", "1"), ("exp_pct", "30"), ("tm_pct", "30"),
            ("hv_pct", "5"), ("hv_add", "100")));
        Assert.Contains("Saved package 5", await r.Content.ReadAsStringAsync());
        var p = site.Db.Packages.Single(x => x.PackageId == 5);
        Assert.Equal((54, 30, 5, 100, true), (p.DurationHours, p.ExpPct, p.HvPct, p.HvAdd, p.Enabled));

        await c.PostAsync("/admin/package", Form(("package_id", "5"), ("name", "Weekend"), ("price", "1200"), ("days", "0"),
            ("hours", "12")));
        p = site.Db.Packages.Single(x => x.PackageId == 5);
        Assert.Equal((1200, 12, 0, false), (p.Price, p.DurationHours, p.ExpPct, p.Enabled));

        r = await c.PostAsync("/admin/package/delete", Form(("package_id", "1")));
        Assert.Contains("it was disabled instead", await r.Content.ReadAsStringAsync());
        Assert.False(site.Db.Packages.Single(x => x.PackageId == 1).Enabled);
        r = await c.PostAsync("/admin/package/delete", Form(("package_id", "5")));
        Assert.Contains("Package 5 deleted.", await r.Content.ReadAsStringAsync());
        Assert.DoesNotContain(site.Db.Packages, x => x.PackageId == 5);
    }

    [Fact]
    public async Task PurchaseLog()
    {
        using var site = new Site();
        var html = Preview.Save("admin_log", await (await site.Admin()).GetStringAsync("/admin/log"));
        Assert.Contains("tester", html);
        Assert.Contains("(was #3)", html);
    }
}

public class ApiTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static async Task<HttpClient> Login(Site site)
    {
        var c = site.Client();
        var r = await c.PostAsJsonAsync("/api/login", new { account = FakeDb.Gid, password = FakeDb.Password });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Json(r);
        Assert.Equal(FakeDb.Code, j.GetProperty("account_code").GetInt32());
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("token").GetString());
        return c;
    }

    [Fact]
    public async Task PackagesUseTheColumnNames()
    {
        using var site = new Site();
        var j = await Json(await site.Client().GetAsync("/api/packages"));
        Assert.Equal(3, j.GetArrayLength());
        var gold = j[1];
        Assert.Equal(2, gold.GetProperty("package_id").GetInt32());
        Assert.Equal(720, gold.GetProperty("duration_hours").GetInt32());
        Assert.Equal(50, gold.GetProperty("exp_pct").GetInt32());
        Assert.Equal(10, gold.GetProperty("ap_pct").GetInt32());
        Assert.Equal(50, gold.GetProperty("ap_add").GetInt32());
        Assert.Equal(500, gold.GetProperty("hp_add").GetInt32());
        Assert.True(gold.GetProperty("enabled").GetBoolean());
        Assert.False(gold.TryGetProperty("main", out _));
    }

    [Fact]
    public async Task LoginCharactersBuyHistory()
    {
        using var site = new Site();
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Client().GetAsync("/api/characters")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await site.Client().PostAsJsonAsync("/api/login", new { account = FakeDb.Gid, password = "x" })).StatusCode);

        var c = await Login(site);
        var me = await Json(await c.GetAsync("/api/me"));
        Assert.Equal(25000, me.GetProperty("cash").GetInt32());
        var chars = await Json(await c.GetAsync("/api/characters"));
        Assert.Equal(3, chars.GetArrayLength());
        Assert.Equal("Gold 30 days", chars[0].GetProperty("package_name").GetString());
        Assert.Equal(JsonValueKind.Null, chars[1].GetProperty("package_id").ValueKind);

        var r = await c.PostAsJsonAsync("/api/buy", new { char_uid = 5001, package_id = 2, expected_price = 10000 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Json(r);
        Assert.Equal(0, j.GetProperty("result").GetInt32());
        Assert.Equal(15000, j.GetProperty("cash_left").GetInt32());
        Assert.Equal(new DateTime(2026, 11, 7, 17, 0, 0), j.GetProperty("end_date").GetDateTime());

        r = await c.PostAsJsonAsync("/api/buy", new { char_uid = 5002, package_id = 2, expected_price = 9000 });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("price_changed", (await Json(r)).GetProperty("error").GetString());
        r = await c.PostAsJsonAsync("/api/buy", new { char_uid = 6001, package_id = 1, expected_price = 3000 });
        Assert.Equal("character_not_owned", (await Json(r)).GetProperty("error").GetString());
        r = await c.PostAsJsonAsync("/api/buy", new { char_uid = 5002, package_id = 2, expected_price = 10000 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        r = await c.PostAsJsonAsync("/api/buy", new { char_uid = 5003, package_id = 2, expected_price = 10000 });
        Assert.Equal("not_enough_points", (await Json(r)).GetProperty("error").GetString());
        Assert.Equal(5000, (await Json(r)).GetProperty("cash_left").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/buy", new StringContent("{oops"))).StatusCode);

        var h = await Json(await c.GetAsync("/api/history?rows=2"));
        Assert.Equal(2, h.GetArrayLength());
        Assert.Equal("new", h[0].GetProperty("kind").GetString());
        Assert.Equal("extend", h[1].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task AdminCrud()
    {
        using var site = new Site();
        var c = site.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/admin/packages")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/admin/login", new { password = "no" })).StatusCode);
        var login = await Json(await c.PostAsJsonAsync("/api/admin/login", new { password = "admin-pw" }));
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("token").GetString());

        Assert.Equal(4, (await Json(await c.GetAsync("/api/admin/packages"))).GetArrayLength());
        var bad = await c.PostAsJsonAsync("/api/admin/packages", new { name = "x", duration_hours = 0, exp_pct = 101 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(2, (await Json(bad)).GetProperty("errors").GetArrayLength());

        var r = await c.PostAsJsonAsync("/api/admin/packages", new { name = "Api", price = 700, duration_hours = 12, tm_pct = 15, dx_add = 20 });
        Assert.Equal(5, (await Json(r)).GetProperty("package_id").GetInt32());
        var p = site.Db.Packages.Single(x => x.PackageId == 5);
        Assert.Equal((12, 15, 20, true), (p.DurationHours, p.TmPct, p.DxAdd, p.Enabled));
        r = await c.PostAsJsonAsync("/api/admin/packages", new { package_id = 77, name = "Api", duration_hours = 12 });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);

        Assert.Equal("deleted", (await Json(await c.DeleteAsync("/api/admin/packages/5"))).GetProperty("result").GetString());
        Assert.Equal("disabled", (await Json(await c.DeleteAsync("/api/admin/packages/2"))).GetProperty("result").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/admin/packages/5")).StatusCode);
        var log = await Json(await c.GetAsync("/api/admin/log?rows=10"));
        Assert.Equal("tester", log[0].GetProperty("account_gid").GetString());

        // the player token is no admin token
        var player = await Login(site);
        Assert.Equal(HttpStatusCode.Unauthorized, (await player.GetAsync("/api/admin/packages")).StatusCode);
    }
}
