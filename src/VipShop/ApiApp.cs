using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace VipShop;

/// <summary>
/// JSON API with the same operations as the pages (README.md "JSON API"). Field names are the SQL column names
/// (snake_case). Login gives a token to send as "Authorization: Bearer &lt;token&gt;"; it expires like the page login.
/// </summary>
public sealed class ApiApp(IVipDb db, Tokens tokens, IOptions<VipOptions> vip, IOptions<AdminOptions> admin,
    IOptions<JsonOptions> json, TimeProvider time, ILogger<ApiApp> log)
{
    private readonly VipOptions _o = vip.Value;
    private readonly AdminOptions _a = admin.Value;
    private readonly JsonSerializerOptions _json = json.Value.SerializerOptions;

    public sealed record LoginRequest(string? Account, string? Password);
    public sealed record AdminLoginRequest(string? Password);
    public sealed record BuyRequest(int CharUid, int PackageId, int ExpectedPrice);

    public void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/packages", async () => Results.Json(await db.PackagesAsync(false)));
        api.MapPost("/login", (Delegate)Login);
        api.MapGet("/me", async (HttpContext ctx) => await Player(ctx) is { } a
            ? Results.Json(new { a.AccountCode, a.AccountGid, a.Cash }) : Unauthorized());
        api.MapGet("/characters", async (HttpContext ctx) => await Player(ctx) is { } a
            ? Results.Json(await db.CharactersAsync(a.AccountCode)) : Unauthorized());
        api.MapPost("/buy", (Delegate)Buy);
        api.MapGet("/history", async (HttpContext ctx, int? rows) => await Player(ctx) is { } a
            ? Results.Json(await db.HistoryAsync(a.AccountCode, Math.Clamp(rows ?? _o.HistoryRows, 1, 1000))) : Unauthorized());

        var adm = api.MapGroup("/admin");
        adm.MapPost("/login", (Delegate)AdminLogin);
        adm.MapGet("/packages", async (HttpContext ctx) => AdminCheck(ctx) ?? Results.Json(await db.PackagesAsync(true)));
        adm.MapPost("/packages", (Delegate)SavePackage);
        adm.MapDelete("/packages/{id:int}", DeletePackage);
        adm.MapGet("/log", async (HttpContext ctx, int? rows) =>
            AdminCheck(ctx) ?? Results.Json(await db.LogAsync(Math.Clamp(rows ?? _a.LogRows, 1, 5000))));
    }

    private static IResult Unauthorized() => Results.Json(new { error = "unauthorized" }, statusCode: 401);
    private static IResult BadRequest(params string[] errors) => Results.Json(new { error = "bad_request", errors }, statusCode: 400);

    private static string? Bearer(HttpContext ctx)
    {
        string h = ctx.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h[7..].Trim() : null;
    }

    private async Task<Account?> Player(HttpContext ctx) =>
        tokens.Player(Bearer(ctx)) is { } s ? await db.AccountAsync(s.AccountCode) : null;

    /// <summary>null when the admin token is valid, else the error to return.</summary>
    private IResult? AdminCheck(HttpContext ctx) =>
        string.IsNullOrEmpty(_a.Password) ? Results.NotFound() : tokens.Admin(Bearer(ctx)) ? null : Unauthorized();

    private async Task<T?> Body<T>(HttpContext ctx) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body, _json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private object TokenReply(string token, TimeSpan life) =>
        new { Token = token, Expires = time.GetLocalNow().DateTime.Add(life) };

    private async Task<IResult> Login(HttpContext ctx)
    {
        var req = await Body<LoginRequest>(ctx);
        if (req is null) return BadRequest("JSON body {\"account\": ..., \"password\": ...} expected");
        string gid = (req.Account ?? "").Trim();
        string? key = gid.Length is > 0 and <= 50 ? LoginKey.Of(req.Password ?? "") : null;
        int code = key is null ? 0 : await db.AuthAsync(gid, key);
        if (code <= 0)
        {
            log.LogWarning("api login refused: {Gid} from {Ip}", gid, ctx.Connection.RemoteIpAddress);
            await Task.Delay(1000);
            return Results.Json(new { error = "login_failed" }, statusCode: 401);
        }
        var life = TimeSpan.FromHours(Math.Max(1, _o.SessionHours));
        log.LogInformation("api login: {Gid} ({Account}) from {Ip}", gid, code, ctx.Connection.RemoteIpAddress);
        return Results.Json(new
        {
            Token = tokens.IssuePlayer(new PlayerSession(code, gid), life),
            AccountCode = code,
            AccountGid = gid,
            Expires = time.GetLocalNow().DateTime.Add(life),
        });
    }

    private async Task<IResult> Buy(HttpContext ctx)
    {
        if (await Player(ctx) is not { } a) return Unauthorized();
        var req = await Body<BuyRequest>(ctx);
        if (req is null || req.CharUid <= 0 || req.PackageId <= 0 || req.ExpectedPrice < 0)
            return BadRequest("JSON body {\"char_uid\": ..., \"package_id\": ..., \"expected_price\": ...} expected");
        var r = await db.BuyAsync(a.AccountCode, req.CharUid, req.PackageId, req.ExpectedPrice);
        log.LogInformation("api buy: account {Account} ({Gid}) char {Char} package {Package} price {Price} -> {Code} cash {Cash} end {End}",
            a.AccountCode, a.AccountGid, req.CharUid, req.PackageId, req.ExpectedPrice, r.Code, r.CashLeft, r.EndDate);
        return Results.Json(new
        {
            Result = r.Code,
            Error = BuyCodes.Name(r.Code),
            Message = BuyCodes.Message(r.Code, _o.PointName),
            r.CashLeft,
            r.EndDate,
        }, statusCode: r.Code == BuyCodes.Ok ? 200 : 409);
    }

    private async Task<IResult> AdminLogin(HttpContext ctx)
    {
        if (string.IsNullOrEmpty(_a.Password)) return Results.NotFound();
        var req = await Body<AdminLoginRequest>(ctx);
        if (req is null || !AdminApp.PasswordMatches(req.Password ?? "", _a.Password))
        {
            log.LogWarning("api admin: wrong password from {Ip}", ctx.Connection.RemoteIpAddress);
            await Task.Delay(1500);
            return Unauthorized();
        }
        var life = TimeSpan.FromHours(Math.Max(1, _a.SessionHours));
        log.LogInformation("api admin: signed in from {Ip}", ctx.Connection.RemoteIpAddress);
        return Results.Json(TokenReply(tokens.IssueAdmin(life), life));
    }

    private async Task<IResult> SavePackage(HttpContext ctx)
    {
        if (AdminCheck(ctx) is { } denied) return denied;
        var p = await Body<VipPackage>(ctx);
        if (p is null) return BadRequest("JSON package object expected (fields as in GET /api/admin/packages)");
        p = p with { Name = (p.Name ?? "").Trim(), Description = (p.Description ?? "").Trim() };
        var errors = PackageRules.Validate(p);
        if (errors.Count > 0) return BadRequest([.. errors]);
        var (code, id) = await db.SavePackageAsync(p);
        log.LogInformation("api admin: save package {Id} '{Name}' -> {Code} id {NewId}", p.PackageId, p.Name, code, id);
        return code switch
        {
            0 => Results.Json(new { PackageId = id }),
            2 => Results.Json(new { error = "not_found" }, statusCode: 404),
            _ => BadRequest("the database refused the values (code " + code + ")"),
        };
    }

    private async Task<IResult> DeletePackage(HttpContext ctx, int id)
    {
        if (AdminCheck(ctx) is { } denied) return denied;
        var r = await db.DeletePackageAsync(id);
        log.LogInformation("api admin: delete package {Id} -> {Result}", id, r);
        return r switch
        {
            DeleteResult.Deleted => Results.Json(new { result = "deleted" }),
            DeleteResult.Disabled => Results.Json(new { result = "disabled" }),
            _ => Results.Json(new { error = "not_found" }, statusCode: 404),
        };
    }
}
