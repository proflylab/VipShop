using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using static VipShop.Html;

namespace VipShop;

/// <summary>
/// Player pages: log in with the game account, see the points and characters, pick a package for a character,
/// confirm (with a warning when it replaces another running package), result, purchase history.
/// The login is a signed cookie (SameSite=Strict, so other sites cannot post forms with it); a purchase needs the
/// one-time token of its confirm page.
/// </summary>
public sealed class PlayerApp(IVipDb db, Tokens tokens, IOptions<VipOptions> options, TimeProvider time, ILogger<PlayerApp> log)
{
    public const string Cookie = "vipshop_session";
    private readonly VipOptions _o = options.Value;

    public void Map(WebApplication app)
    {
        app.MapGet("/", Home);
        app.MapPost("/login", Login);
        app.MapPost("/logout", Logout);
        app.MapGet("/packages", Packages);
        app.MapGet("/buy", Confirm);
        app.MapPost("/buy", Buy);
        app.MapGet("/history", History);
    }

    private DateTime Now => time.GetLocalNow().DateTime;
    private PlayerSession? Session(HttpContext ctx) => tokens.Player(ctx.Request.Cookies[Cookie]);

    /// <summary>The session and its account; null after sending the login page (not logged in / account gone).</summary>
    private async Task<(PlayerSession S, Account A)?> Require(HttpContext ctx)
    {
        var s = Session(ctx);
        var a = s is null ? null : await db.AccountAsync(s.AccountCode);
        if (s is not null && a is not null) return (s, a);
        ctx.Response.Cookies.Delete(Cookie);
        await Send(ctx, LoginPage(null), s is null ? 200 : 401);
        return null;
    }

    private async Task Home(HttpContext ctx)
    {
        if (await Require(ctx) is not var (s, a)) return;
        var chars = await db.CharactersAsync(a.AccountCode);
        var packages = await db.PackagesAsync(false);
        var now = Now;
        var sb = new StringBuilder();
        sb.Append(Msg(ctx.Request.Query["msg"].ToString()));
        sb.Append("<section class=\"card\"><h2>My characters</h2>");
        if (chars.Count == 0) sb.Append("<p class=\"muted\">No character on this account yet. Create one in the game first.</p>");
        else
        {
            sb.Append("<table><thead><tr><th>Character</th><th class=\"n\">Level</th><th class=\"n\">Job</th><th>VIP</th></tr></thead><tbody>");
            foreach (var c in chars)
                sb.Append("<tr><td><b>").Append(E(c.CharName)).Append("</b></td><td class=\"n\">").Append(c.CharLevel)
                  .Append("</td><td class=\"n\">").Append(c.Job).Append("</td><td>").Append(CharVip(c, now)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        sb.Append("</section><h2 class=\"section\">VIP packages</h2>").Append(PackageGrid(packages, chars));
        await Send(ctx, Page(_o.SiteName, Header(s, a), sb.ToString()));
    }

    private async Task Packages(HttpContext ctx)
    {
        var s = Session(ctx);
        var a = s is null ? null : await db.AccountAsync(s.AccountCode);
        var chars = a is null ? null : await db.CharactersAsync(a.AccountCode);
        var packages = await db.PackagesAsync(false);
        await Send(ctx, Page("VIP packages · " + _o.SiteName, Header(a is null ? null : s, a),
            "<h2 class=\"section\">VIP packages</h2>" + PackageGrid(packages, chars)));
    }

    private string PackageGrid(List<VipPackage> packages, List<VipChar>? chars)
    {
        if (packages.Count == 0) return Empty(Icon.Crown, "No VIP package is on sale right now.");
        var sb = new StringBuilder("<div class=\"pkgs\">");
        foreach (var p in packages)
        {
            sb.Append("<article class=\"card pkg\"><div class=\"pkg-head\"><h3>").Append(Icon.Crown).Append(E(p.Name))
              .Append("</h3><span class=\"price\">").Append(Icon.Coin).Append(N(p.Price)).Append(" <small>").Append(E(_o.PointName))
              .Append("</small></span></div><p class=\"dur\">").Append(Icon.Clock).Append(Duration.Text(p.DurationHours)).Append("</p>");
            if (p.Description.Length > 0) sb.Append("<p class=\"desc\">").Append(E(p.Description)).Append("</p>");
            sb.Append(Effects(p));
            if (chars is null)
                sb.Append("<a class=\"btn\" href=\"/\">Log in to buy</a>");
            else if (chars.Count == 0)
                sb.Append("<p class=\"muted\">Create a character in the game to buy.</p>");
            else
            {
                sb.Append("<form method=\"get\" action=\"/buy\" class=\"buy\">").Append(Hidden("package", p.PackageId.ToString(CultureInfo.InvariantCulture)))
                  .Append("<select name=\"char\" aria-label=\"Character\">");
                foreach (var c in chars)
                    sb.Append("<option value=\"").Append(c.CharUid).Append("\">").Append(E(c.CharName)).Append(" (Lv ")
                      .Append(c.CharLevel).Append(")</option>");
                sb.Append("</select><button class=\"btn\">Buy</button></form>");
            }
            sb.Append("</article>");
        }
        return sb.Append("</div>").ToString();
    }

    private async Task Confirm(HttpContext ctx)
    {
        if (await Require(ctx) is not var (s, a)) return;
        int.TryParse(ctx.Request.Query["char"], NumberStyles.None, CultureInfo.InvariantCulture, out var charUid);
        int.TryParse(ctx.Request.Query["package"], NumberStyles.None, CultureInfo.InvariantCulture, out var packageId);
        var c = (await db.CharactersAsync(a.AccountCode)).FirstOrDefault(x => x.CharUid == charUid);
        var p = (await db.PackagesAsync(false)).FirstOrDefault(x => x.PackageId == packageId);
        if (c is null || p is null)
        {
            await Send(ctx, Problem(s, a, c is null ? BuyCodes.Message(BuyCodes.NotYourCharacter, _o.PointName)
                : BuyCodes.Message(BuyCodes.PackageUnavailable, _o.PointName)), 404);
            return;
        }
        var now = Now;
        var plan = BuyPlan.Of(c, p, a.Cash, now);
        var sb = new StringBuilder("<section class=\"card confirm\"><h2>").Append(Icon.Crown).Append("Confirm purchase</h2><dl class=\"sum\">")
            .Append("<dt>Character</dt><dd><b>").Append(E(c.CharName)).Append("</b> (Lv ").Append(c.CharLevel).Append(")</dd>")
            .Append("<dt>Package</dt><dd><b>").Append(E(p.Name)).Append("</b></dd>")
            .Append("<dt>Duration</dt><dd>").Append(Duration.Text(p.DurationHours)).Append("</dd>")
            .Append("<dt>Bonuses</dt><dd>").Append(Effects(p)).Append("</dd>")
            .Append("<dt>Price</dt><dd><b>").Append(N(p.Price)).Append("</b> ").Append(E(_o.PointName)).Append("</dd>")
            .Append("<dt>Your points</dt><dd>").Append(N(a.Cash)).Append("</dd>")
            .Append("<dt>After purchase</dt><dd class=\"").Append(plan.Enough ? "plus" : "minus").Append("\">")
            .Append(plan.Enough ? N(plan.CashAfter) : "-").Append("</dd></dl>");
        string end = Duration.Date(plan.EndsAbout);
        switch (plan.Kind)
        {
            case BuyKind.New:
                sb.Append(Msg($"The package starts now and ends at about {end}."));
                break;
            case BuyKind.Extend:
                sb.Append(Msg($"{c.CharName} already has {p.Name} until {Duration.Date(c.EndDate!.Value)}. " +
                              $"This adds {Duration.Text(p.DurationHours)}: it will end at about {end}."));
                break;
            case BuyKind.Replace:
                sb.Append("<div class=\"warn\"><b>").Append(Icon.Oops).Append("This replaces the current package</b><p>")
                  .Append(E(c.CharName)).Append(" has <b>").Append(E(c.PackageName)).Append("</b> until ")
                  .Append(Duration.Date(c.EndDate!.Value)).Append(" (").Append(Duration.Left(c.EndDate.Value, now))
                  .Append(" left). Buying <b>").Append(E(p.Name)).Append("</b> ends it now and its remaining time is lost. ")
                  .Append("The new package ends at about ").Append(end).Append(".</p></div>");
                break;
        }
        if (!plan.Enough)
        {
            sb.Append(Msg($"Not enough {_o.PointName}: you need {N(p.Price - a.Cash)} more.", error: true))
              .Append("<div class=\"actions\"><a class=\"btn ghost\" href=\"/\">Back</a></div>");
        }
        else
        {
            var token = tokens.IssueBuy(new BuyTicket(a.AccountCode, c.CharUid, p.PackageId, p.Price),
                TimeSpan.FromMinutes(Math.Max(1, _o.ConfirmMinutes)));
            sb.Append("<form method=\"post\" action=\"/buy\" class=\"actions\">").Append(Hidden("token", token));
            if (plan.Kind == BuyKind.Replace)
                sb.Append("<label class=\"check\"><input type=\"checkbox\" name=\"replace\" value=\"1\" required> I understand that ")
                  .Append(E(c.PackageName)).Append(" ends now</label>");
            sb.Append("<a class=\"btn ghost\" href=\"/\">Cancel</a><button class=\"btn\">Buy for ").Append(N(p.Price)).Append(' ')
              .Append(E(_o.PointName)).Append("</button></form>");
        }
        sb.Append("</section>");
        await Send(ctx, Page("Confirm · " + _o.SiteName, Header(s, a), sb.ToString()));
    }

    private async Task Buy(HttpContext ctx)
    {
        if (await Require(ctx) is not var (s, a)) return;
        var form = await ctx.Request.ReadFormAsync();
        var t = tokens.RedeemBuy(form["token"], a.AccountCode);
        if (t is null)
        {
            await Send(ctx, Problem(s, a, "This confirm page has expired or was already used. Nothing was bought by it; " +
                                          "check your characters and buy again if needed."), 400);
            return;
        }
        var r = await db.BuyAsync(t.AccountCode, t.CharUid, t.PackageId, t.Price);
        log.LogInformation("buy: account {Account} ({Gid}) char {Char} package {Package} price {Price} -> {Code} cash {Cash} end {End}",
            a.AccountCode, a.AccountGid, t.CharUid, t.PackageId, t.Price, r.Code, r.CashLeft, r.EndDate);
        var acc = a with { Cash = r.CashLeft };
        if (r.Code != BuyCodes.Ok)
        {
            string again = r.Code == BuyCodes.PriceChanged ? $"/buy?char={t.CharUid}&package={t.PackageId}" : "/";
            await Send(ctx, Page("Purchase failed · " + _o.SiteName, Header(s, acc),
                "<section class=\"card result\">" + Icon.Oops + "<h2>Purchase failed</h2>" +
                Msg(BuyCodes.Message(r.Code, _o.PointName), error: true) +
                "<div class=\"actions\"><a class=\"btn\" href=\"" + E(again) + "\">" +
                (r.Code == BuyCodes.PriceChanged ? "See the new price" : "Back") + "</a></div></section>"), 409);
            return;
        }
        var c = (await db.CharactersAsync(a.AccountCode)).FirstOrDefault(x => x.CharUid == t.CharUid);
        await Send(ctx, Page("Purchase complete · " + _o.SiteName, Header(s, acc),
            "<section class=\"card result\">" + Icon.Crown + "<h2>Purchase complete</h2><p><b>" + E(c?.PackageName) +
            "</b> is active on <b>" + E(c?.CharName) + "</b> until <b>" + (r.EndDate is DateTime d ? Duration.Date(d) : "-") +
            "</b>.</p><p class=\"muted\">" + E(_o.PointName) + " left: " + N(r.CashLeft) + "</p><p class=\"muted\">" + E(GameNote) +
            "</p><div class=\"actions\"><a class=\"btn ghost\" href=\"/history\">History</a><a class=\"btn\" href=\"/\">My characters</a></div></section>"));
    }

    /// <summary>When the bonuses reach the game (TrickMod GameServer module vip).</summary>
    internal const string GameNote = "In the game: a character that is online gets the bonuses within about a minute (at once " +
        "after a map change or a new login), with a chat message showing the package and its end time.";

    private async Task History(HttpContext ctx)
    {
        if (await Require(ctx) is not var (s, a)) return;
        var lines = await db.HistoryAsync(a.AccountCode, Math.Clamp(_o.HistoryRows, 1, 1000));
        var sb = new StringBuilder("<section class=\"card\"><h2>Purchase history</h2>");
        if (lines.Count == 0) sb.Append("<p class=\"muted\">No purchase yet.</p>");
        else
        {
            sb.Append("<table><thead><tr><th>Date</th><th>Character</th><th>Package</th><th>Type</th><th>Duration</th>")
              .Append("<th class=\"n\">Price</th><th>Ends</th><th class=\"n\">").Append(E(_o.PointName)).Append(" left</th></tr></thead><tbody>");
            foreach (var l in lines)
                sb.Append("<tr><td>").Append(Duration.Date(l.LogDate)).Append("</td><td>").Append(E(l.CharName)).Append("</td><td>")
                  .Append(E(l.PackageName)).Append("</td><td><span class=\"tag ").Append(E(l.Kind)).Append("\">").Append(E(l.Kind))
                  .Append("</span></td><td>").Append(Duration.Text(l.DurationHours)).Append("</td><td class=\"n minus\">-")
                  .Append(N(l.Price)).Append("</td><td>").Append(l.EndDate is DateTime d ? Duration.Date(d) : "-")
                  .Append("</td><td class=\"n\">").Append(N(l.CashLeft)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        sb.Append("</section>");
        await Send(ctx, Page("History · " + _o.SiteName, Header(s, a), sb.ToString()));
    }

    private async Task Login(HttpContext ctx)
    {
        var form = await ctx.Request.ReadFormAsync();
        string gid = (form["account"].ToString() ?? "").Trim();
        string password = form["password"].ToString() ?? "";
        string? key = gid.Length is > 0 and <= 50 ? LoginKey.Of(password) : null;
        int code = key is null ? 0 : await db.AuthAsync(gid, key);
        if (code <= 0)
        {
            log.LogWarning("login refused: {Gid} from {Ip}", gid, ctx.Connection.RemoteIpAddress);
            await Task.Delay(1000);
            await Send(ctx, LoginPage("Wrong account or password."), 401);
            return;
        }
        var life = TimeSpan.FromHours(Math.Max(1, _o.SessionHours));
        ctx.Response.Cookies.Append(Cookie, tokens.IssuePlayer(new PlayerSession(code, gid), life),
            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = life });
        log.LogInformation("login: {Gid} ({Account}) from {Ip}", gid, code, ctx.Connection.RemoteIpAddress);
        ctx.Response.Redirect("/");
    }

    private static Task Logout(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(Cookie, new CookieOptions { Path = "/" });
        ctx.Response.Redirect("/");
        return Task.CompletedTask;
    }

    private string LoginPage(string? error) => Page(_o.SiteName, Header(null, null),
        "<form class=\"card login\" method=\"post\" action=\"/login\"><div class=\"hero\">" + Icon.Crown + "<h1>" + E(_o.SiteName) +
        "</h1><p>Log in with your game account to buy VIP for your characters.</p></div>" + Msg(error, error: true) +
        "<label>Account<input name=\"account\" maxlength=\"50\" autocomplete=\"username\" autofocus required></label>" +
        "<label>Password<input type=\"password\" name=\"password\" autocomplete=\"current-password\" required></label>" +
        "<button class=\"btn\">Log in</button><p class=\"muted center\"><a href=\"/packages\">See the packages</a></p></form>");

    private string Problem(PlayerSession s, Account a, string text) => Page(_o.SiteName, Header(s, a),
        "<section class=\"card result\">" + Icon.Oops + "<h2>Cannot buy</h2>" + Msg(text, error: true) +
        "<div class=\"actions\"><a class=\"btn\" href=\"/\">Back</a></div></section>");

    private string Header(PlayerSession? s, Account? a)
    {
        var sb = new StringBuilder("<header class=\"bar\"><a class=\"brand\" href=\"/\">").Append(Icon.Crown).Append(E(_o.SiteName))
            .Append("</a><nav>");
        if (s is not null) sb.Append("<a href=\"/\">My characters</a>");
        sb.Append("<a href=\"/packages\">Packages</a>");
        if (s is not null) sb.Append("<a href=\"/history\">History</a>");
        sb.Append("</nav>");
        if (s is not null && a is not null)
            sb.Append(Points(a.Cash, _o.PointName)).Append("<span class=\"who\">").Append(E(a.AccountGid)).Append("</span>")
              .Append("<form method=\"post\" action=\"/logout\"><button class=\"btn ghost\">Log out</button></form>");
        else
            sb.Append("<a class=\"btn ghost\" href=\"/\">Log in</a>");
        return sb.Append("</header>").ToString();
    }

    internal static Task Send(HttpContext ctx, string html, int status = 200)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        return ctx.Response.WriteAsync(html);
    }
}
