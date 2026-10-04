namespace VipShop;

/// <summary>appsettings.json section "Vip": the player pages and the player API.</summary>
public sealed class VipOptions
{
    /// <summary>Title in the page header.</summary>
    public string SiteName { get; set; } = "Trickster VIP";

    /// <summary>Name of the points on the pages (the MyShop cash balance, gmg_account.dbo.tbl_cash).</summary>
    public string PointName { get; set; } = "Cash";

    /// <summary>Key that signs the session cookies, API tokens and confirm forms; empty = a random key per start
    /// (everybody is logged out by a restart).</summary>
    public string Secret { get; set; } = "";

    /// <summary>Hours a player login (cookie or API token) stays valid.</summary>
    public int SessionHours { get; set; } = 4;

    /// <summary>Minutes a buy confirm page stays valid.</summary>
    public int ConfirmMinutes { get; set; } = 15;

    /// <summary>Purchases shown on the history page.</summary>
    public int HistoryRows { get; set; } = 50;
}

/// <summary>appsettings.json section "Admin": the /admin pages and /api/admin.</summary>
public sealed class AdminOptions
{
    /// <summary>Empty = /admin and /api/admin are switched off (404).</summary>
    public string Password { get; set; } = "";
    public int SessionHours { get; set; } = 8;

    /// <summary>Purchases shown on the admin purchase log.</summary>
    public int LogRows { get; set; } = 200;
}
