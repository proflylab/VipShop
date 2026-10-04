using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using VipShop;

// VipShop: sample web to buy VIP packages for Trickster characters with MyShop points (README.md).
// One self-contained folder: VipShop.exe + appsettings.json + wwwroot, started by Start.cmd.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(AppContext.BaseDirectory, "logs")));
builder.Services.Configure<VipOptions>(builder.Configuration.GetSection("Vip"));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IVipDb>(sp => new SqlVipDb(sp.GetRequiredService<IConfiguration>().GetConnectionString("Account")
    ?? throw new InvalidOperationException("appsettings.json: ConnectionStrings:Account is missing")));
builder.Services.AddSingleton(sp => new Tokens(sp.GetRequiredService<IOptions<VipOptions>>().Value.Secret,
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<PlayerApp>();
builder.Services.AddSingleton<AdminApp>();
builder.Services.AddSingleton<ApiApp>();

var app = builder.Build();
var log = app.Services.GetRequiredService<ILogger<Program>>();

app.Use((ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    return next(ctx);
});
// The error is logged by the middleware itself; the visitor only gets a short page / JSON (503 = database unreachable).
app.UseExceptionHandler(a => a.Run(ctx =>
{
    bool database = ctx.Features.Get<IExceptionHandlerFeature>()?.Error is SqlException;
    ctx.Response.StatusCode = database ? 503 : 500;
    if (ctx.Request.Path.StartsWithSegments("/api"))
        return ctx.Response.WriteAsJsonAsync(new { error = database ? "database" : "server" });
    return PlayerApp.Send(ctx, Html.Page("Error", "<header class=\"bar\"><a class=\"brand\" href=\"/\">" + Html.Icon.Crown + "VipShop</a></header>",
        Html.Empty(Html.Icon.Oops, database ? "The database is not reachable right now. Please try again later."
                                            : "Something went wrong. Please try again later.")), ctx.Response.StatusCode);
}));

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets")),
    RequestPath = "/assets",
});

app.Services.GetRequiredService<PlayerApp>().Map(app);
app.Services.GetRequiredService<AdminApp>().Map(app);
app.Services.GetRequiredService<ApiApp>().Map(app);

app.Lifetime.ApplicationStarted.Register(() =>
    log.LogInformation("VipShop {Version} listening on {Urls}, admin {Admin}", typeof(Program).Assembly.GetName().Version,
        string.Join(", ", app.Urls),
        string.IsNullOrEmpty(app.Services.GetRequiredService<IOptions<AdminOptions>>().Value.Password) ? "off" : "on"));
app.Run();

/// <summary>Entry point; public for the tests (WebApplicationFactory).</summary>
public partial class Program;
