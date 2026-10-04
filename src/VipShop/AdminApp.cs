using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using static VipShop.Html;

namespace VipShop;

/// <summary>
/// /admin: list, create, edit, disable / delete VIP packages and read the purchase log. Locked with Admin:Password
/// (empty = switched off, 404). The session is a signed cookie limited to /admin (SameSite=Strict).
/// </summary>
public sealed class AdminApp(IVipDb db, Tokens tokens, IOptions<AdminOptions> admin, IOptions<VipOptions> vip,
    ILogger<AdminApp> log)
{
    public const string Cookie = "vipshop_admin";
    private readonly AdminOptions _a = admin.Value;
    private readonly VipOptions _o = vip.Value;

    public void Map(WebApplication app)
    {
        app.MapGet("/admin", Home);
        app.MapPost("/admin/login", Login);
        app.MapPost("/admin/logout", Logout);
        app.MapGet("/admin/package", Edit);
        app.MapPost("/admin/package", Save);
        app.MapPost("/admin/package/delete", Delete);
        app.MapGet("/admin/log", Log);
    }

    private bool Enabled => !string.IsNullOrEmpty(_a.Password);

    /// <summary>True when the request may go on; otherwise the 404 / login page has been sent.</summary>
    private async Task<bool> Allowed(HttpContext ctx)
    {
        if (!Enabled) { ctx.Response.StatusCode = 404; return false; }
        if (tokens.Admin(ctx.Request.Cookies[Cookie])) return true;
        if (HttpMethods.IsGet(ctx.Request.Method)) await PlayerApp.Send(ctx, LoginPage(null));
        else ctx.Response.StatusCode = 403;
        return false;
    }

    /// <summary>Constant-time check of the admin password (also used by /api/admin/login).</summary>
    internal static bool PasswordMatches(string given, string want)
    {
        var g = Encoding.UTF8.GetBytes(given);
        var w = Encoding.UTF8.GetBytes(want);
        return w.Length > 0 && g.Length == w.Length && CryptographicOperations.FixedTimeEquals(g, w);
    }

    private async Task Login(HttpContext ctx)
    {
        if (!Enabled) { ctx.Response.StatusCode = 404; return; }
        var form = await ctx.Request.ReadFormAsync();
        if (!PasswordMatches(form["password"].ToString() ?? "", _a.Password))
        {
            log.LogWarning("admin: wrong password from {Ip}", ctx.Connection.RemoteIpAddress);
            await Task.Delay(1500);
            await PlayerApp.Send(ctx, LoginPage("Wrong password."), 401);
            return;
        }
        ctx.Response.Cookies.Append(Cookie, tokens.IssueAdmin(TimeSpan.FromHours(Math.Max(1, _a.SessionHours))),
            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/admin" });
        log.LogInformation("admin: signed in from {Ip}", ctx.Connection.RemoteIpAddress);
        ctx.Response.Redirect("/admin");
    }

    private static Task Logout(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(Cookie, new CookieOptions { Path = "/admin" });
        ctx.Response.Redirect("/admin");
        return Task.CompletedTask;
    }

    private async Task Home(HttpContext ctx)
    {
        if (!await Allowed(ctx)) return;
        var packages = await db.PackagesAsync(true);
        var sb = new StringBuilder(Msg(ctx.Request.Query["msg"].ToString()));
        sb.Append("<section class=\"card\"><div class=\"card-head\"><h2>VIP packages</h2><a class=\"btn\" href=\"/admin/package\">New package</a></div>");
        if (packages.Count == 0) sb.Append("<p class=\"muted\">No package yet.</p>");
        else
        {
            sb.Append("<table><thead><tr><th class=\"n\">#</th><th>Name</th><th class=\"n\">Price</th><th>Duration</th><th>Bonuses</th>")
              .Append("<th class=\"n\">Sort</th><th>Status</th><th>Updated</th><th></th></tr></thead><tbody>");
            foreach (var p in packages)
            {
                sb.Append("<tr").Append(p.Enabled ? "" : " class=\"off\"").Append("><td class=\"n\">").Append(p.PackageId)
                  .Append("</td><td><b>").Append(E(p.Name)).Append("</b></td><td class=\"n\">").Append(N(p.Price))
                  .Append("</td><td>").Append(Duration.Text(p.DurationHours)).Append("</td><td>").Append(Effects(p))
                  .Append("</td><td class=\"n\">").Append(p.Sort).Append("</td><td>")
                  .Append(p.Enabled ? "<span class=\"tag new\">on sale</span>" : "<span class=\"tag off\">disabled</span>")
                  .Append("</td><td>").Append(p.UpdateDate is DateTime d ? Duration.Date(d) : "").Append("</td><td><div class=\"row-actions\">")
                  .Append("<a class=\"chip\" href=\"/admin/package?id=").Append(p.PackageId).Append("\">Edit</a>")
                  .Append("<form method=\"post\" action=\"/admin/package/delete\" onsubmit=\"return confirm('Delete package ")
                  .Append(p.PackageId).Append("? A package that was bought is disabled instead.')\">")
                  .Append(Hidden("package_id", p.PackageId.ToString(CultureInfo.InvariantCulture)))
                  .Append("<button class=\"chip danger\">Delete</button></form></div></td></tr>");
            }
            sb.Append("</tbody></table>");
        }
        sb.Append("<p class=\"muted small\">Delete removes a package nobody has bought; a package with purchases is disabled instead ")
          .Append("(no longer sold, characters keep it until it ends). Edited bonuses reach characters that already have the package ")
          .Append("within a few seconds.</p></section>");
        await PlayerApp.Send(ctx, Page("VipShop Admin", Header(), sb.ToString()));
    }

    private async Task Edit(HttpContext ctx)
    {
        if (!await Allowed(ctx)) return;
        var p = new VipPackage { DurationHours = 24 * 30 };
        if (int.TryParse(ctx.Request.Query["id"], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            var found = (await db.PackagesAsync(true)).FirstOrDefault(x => x.PackageId == id);
            if (found is null) { ctx.Response.Redirect("/admin?msg=" + Uri.EscapeDataString($"Package {id} not found.")); return; }
            p = found;
        }
        await PlayerApp.Send(ctx, FormPage(p, p.DurationHours / 24, p.DurationHours % 24, []));
    }

    private async Task Save(HttpContext ctx)
    {
        if (!await Allowed(ctx)) return;
        var form = await ctx.Request.ReadFormAsync();
        var (p, errors) = PackageRules.FromForm(name => form.TryGetValue(name, out var v) ? v.ToString() : null);
        int.TryParse(form["days"], out var days);
        int.TryParse(form["hours"], out var hours);
        if (errors.Count > 0)
        {
            await PlayerApp.Send(ctx, FormPage(p, days, hours, errors), 400);
            return;
        }
        var (code, id) = await db.SavePackageAsync(p);
        log.LogInformation("admin: save package {Id} '{Name}' -> {Code} id {NewId}", p.PackageId, p.Name, code, id);
        if (code != 0)
        {
            await PlayerApp.Send(ctx, FormPage(p, days, hours,
                [code == 2 ? $"Package {p.PackageId} no longer exists." : "The database refused the values (code " + code + ")."]), 400);
            return;
        }
        ctx.Response.Redirect("/admin?msg=" + Uri.EscapeDataString($"Saved package {id} \"{p.Name}\"."));
    }

    private async Task Delete(HttpContext ctx)
    {
        if (!await Allowed(ctx)) return;
        var form = await ctx.Request.ReadFormAsync();
        int.TryParse(form["package_id"], NumberStyles.None, CultureInfo.InvariantCulture, out var id);
        var r = await db.DeletePackageAsync(id);
        log.LogInformation("admin: delete package {Id} -> {Result}", id, r);
        string msg = r switch
        {
            DeleteResult.Deleted => $"Package {id} deleted.",
            DeleteResult.Disabled => $"Package {id} has purchases: it was disabled instead (no longer sold).",
            DeleteResult.NotFound => $"Package {id} not found.",
            _ => $"Package {id}: unexpected result {(int)r}.",
        };
        ctx.Response.Redirect("/admin?msg=" + Uri.EscapeDataString(msg));
    }

    private async Task Log(HttpContext ctx)
    {
        if (!await Allowed(ctx)) return;
        var lines = await db.LogAsync(Math.Clamp(_a.LogRows, 1, 5000));
        var sb = new StringBuilder("<section class=\"card\"><h2>Purchase log <small>last ").Append(lines.Count).Append("</small></h2>");
        if (lines.Count == 0) sb.Append("<p class=\"muted\">No purchase yet.</p>");
        else
        {
            sb.Append("<table><thead><tr><th>Date</th><th>Account</th><th>Character</th><th>Package</th><th>Type</th>")
              .Append("<th>Duration</th><th class=\"n\">Price</th><th>Ends</th><th class=\"n\">").Append(E(_o.PointName))
              .Append(" left</th></tr></thead><tbody>");
            foreach (var l in lines)
                sb.Append("<tr><td>").Append(Duration.Date(l.LogDate)).Append("</td><td>").Append(E(l.AccountGid))
                  .Append(" <small class=\"muted\">#").Append(l.AccountCode).Append("</small></td><td>").Append(E(l.CharName))
                  .Append(" <small class=\"muted\">#").Append(l.CharUid).Append("</small></td><td>").Append(E(l.PackageName))
                  .Append(" <small class=\"muted\">#").Append(l.PackageId).Append("</small></td><td><span class=\"tag ").Append(E(l.Kind))
                  .Append("\">").Append(E(l.Kind)).Append("</span>")
                  .Append(l.ReplacedPackage is int rp ? " <small class=\"muted\">(was #" + rp + ")</small>" : "")
                  .Append("</td><td>").Append(Duration.Text(l.DurationHours)).Append("</td><td class=\"n\">").Append(N(l.Price))
                  .Append("</td><td>").Append(l.EndDate is DateTime d ? Duration.Date(d) : "-").Append("</td><td class=\"n\">")
                  .Append(N(l.CashLeft)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        sb.Append("</section>");
        await PlayerApp.Send(ctx, Page("Purchase log · VipShop Admin", Header(), sb.ToString()));
    }

    private string FormPage(VipPackage p, int days, int hours, List<string> errors)
    {
        static string Num(string name, long value, int min, int max, string? label = null) =>
            "<label>" + E(label ?? name) + "<input type=\"number\" name=\"" + name + "\" value=\"" + value + "\" min=\"" + min +
            "\" max=\"" + max + "\" step=\"1\"></label>";

        bool isNew = p.PackageId == 0;
        var sb = new StringBuilder("<form method=\"post\" action=\"/admin/package\" class=\"card pkgform\"><h2>")
            .Append(isNew ? "New package" : "Edit package #" + p.PackageId).Append("</h2>");
        if (errors.Count > 0)
        {
            sb.Append("<ul class=\"msg err\">");
            foreach (var e in errors) sb.Append("<li>").Append(E(e)).Append("</li>");
            sb.Append("</ul>");
        }
        sb.Append(Hidden("package_id", p.PackageId.ToString(CultureInfo.InvariantCulture)))
          .Append("<div class=\"row\"><label class=\"wide\">Name<input name=\"name\" maxlength=\"").Append(PackageRules.MaxName)
          .Append("\" required value=\"").Append(E(p.Name)).Append("\"></label>")
          .Append(Num("price", p.Price, 0, PackageRules.MaxPrice, "Price (" + _o.PointName + ")")).Append("</div>")
          .Append("<label>Description<textarea name=\"description\" rows=\"2\" maxlength=\"").Append(PackageRules.MaxDescription)
          .Append("\">").Append(E(p.Description)).Append("</textarea></label>")
          .Append("<div class=\"row\">").Append(Num("days", days, 0, PackageRules.MaxDays, "Duration: days"))
          .Append(Num("hours", hours, 0, 23, "+ hours")).Append(Num("sort", p.Sort, int.MinValue, int.MaxValue, "Sort (small first)"))
          .Append("<label class=\"check\"><input type=\"checkbox\" name=\"enabled\" value=\"1\"").Append(p.Enabled ? " checked" : "")
          .Append("> On sale</label></div>")
          .Append("<h3>Bonuses <small>% (0-").Append(VipPackage.MaxPct).Append(")</small></h3><div class=\"row\">");
        var main = p.Main;
        for (int i = 0; i < main.Length; i++)
            sb.Append(Num(VipPackage.MainColumns[i], main[i], 0, VipPackage.MaxPct, VipPackage.MainLabels[i] + " %"));
        sb.Append("</div><h3>Stats <small>% (0-").Append(VipPackage.MaxPct).Append(") and flat + (0-").Append(N(VipPackage.MaxAdd))
          .Append(")</small></h3><table class=\"stats\"><thead><tr><th>Stat</th><th>%</th><th>+</th><th>Stat</th><th>%</th><th>+</th></tr></thead><tbody>");
        var pct = p.StatPct;
        var add = p.StatAdd;
        for (int row = 0; row < 6; row++)
        {
            sb.Append("<tr>");
            foreach (int i in new[] { row, row + 6 })
                sb.Append("<th>").Append(VipPackage.Stats[i]).Append("</th><td><input type=\"number\" name=\"").Append(VipPackage.PctColumn(i))
                  .Append("\" value=\"").Append(pct[i]).Append("\" min=\"0\" max=\"").Append(VipPackage.MaxPct)
                  .Append("\" step=\"1\" aria-label=\"").Append(VipPackage.Stats[i]).Append(" %\"></td><td><input type=\"number\" name=\"")
                  .Append(VipPackage.AddColumn(i)).Append("\" value=\"").Append(add[i]).Append("\" min=\"0\" max=\"").Append(VipPackage.MaxAdd)
                  .Append("\" step=\"1\" aria-label=\"").Append(VipPackage.Stats[i]).Append(" +\"></td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table><div class=\"actions\"><a class=\"btn ghost\" href=\"/admin\">Cancel</a><button class=\"btn\">Save</button></div></form>");
        return Page((isNew ? "New package" : "Edit package") + " · VipShop Admin", Header(), sb.ToString());
    }

    private static string LoginPage(string? error) => Page("VipShop Admin",
        "<header class=\"bar\"><span class=\"brand\">" + Icon.Sparkle + "VipShop Admin</span></header>",
        "<form class=\"card login\" method=\"post\" action=\"/admin/login\"><div class=\"hero\">" + Icon.Sparkle +
        "<h1>VipShop Admin</h1><p>VIP packages and purchases</p></div>" + Msg(error, error: true) +
        "<label>Password<input type=\"password\" name=\"password\" autofocus required></label><button class=\"btn\">Log in</button></form>");

    private static string Header() =>
        "<header class=\"bar\"><a class=\"brand\" href=\"/admin\">" + Icon.Sparkle + "VipShop Admin</a><nav>" +
        "<a href=\"/admin\">Packages</a><a href=\"/admin/package\">New package</a><a href=\"/admin/log\">Purchase log</a></nav>" +
        "<form method=\"post\" action=\"/admin/logout\"><button class=\"btn ghost\">Log out</button></form></header>";
}
