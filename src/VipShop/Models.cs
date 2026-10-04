using System.Text.Json.Serialization;

namespace VipShop;

/// <summary>
/// One row of trickster.dbo.tbl_vip_package (gmg_account.dbo.uspw_vip_package_list). The JSON API writes and reads
/// the same names as the table columns (snake_case: package_id, duration_hours, exp_pct, ap_pct, ap_add ...).
/// Percents are 0..100, flat stat bonuses (*_add) 0..10000, duration in hours (> 0).
/// </summary>
public sealed record VipPackage
{
    public int PackageId { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Price { get; init; }
    public int DurationHours { get; init; }

    public int ExpPct { get; init; }
    public int TmPct { get; init; }
    public int SpeedPct { get; init; }
    public int FindPct { get; init; }

    public int ApPct { get; init; }
    public int AcPct { get; init; }
    public int DxPct { get; init; }
    public int MpPct { get; init; }
    public int MaPct { get; init; }
    public int MdPct { get; init; }
    public int WtPct { get; init; }
    public int DaPct { get; init; }
    public int LkPct { get; init; }
    public int HpPct { get; init; }
    public int DpPct { get; init; }
    public int HvPct { get; init; }

    public int ApAdd { get; init; }
    public int AcAdd { get; init; }
    public int DxAdd { get; init; }
    public int MpAdd { get; init; }
    public int MaAdd { get; init; }
    public int MdAdd { get; init; }
    public int WtAdd { get; init; }
    public int DaAdd { get; init; }
    public int LkAdd { get; init; }
    public int HpAdd { get; init; }
    public int DpAdd { get; init; }
    public int HvAdd { get; init; }

    public bool Enabled { get; init; } = true;
    public int Sort { get; init; }
    public DateTime? UpdateDate { get; init; }

    /// <summary>Column / form / JSON names of <see cref="Main"/>. find_pct = monster drop rate only (digging is not affected).</summary>
    public static readonly string[] MainColumns = ["exp_pct", "tm_pct", "speed_pct", "find_pct"];
    public static readonly string[] MainLabels = ["EXP", "TM", "Run speed", "Drop rate"];

    /// <summary>The 12 stats in the order of the GM Ability packet; columns are &lt;stat&gt;_pct and &lt;stat&gt;_add.</summary>
    public static readonly string[] Stats = ["AP", "AC", "DX", "MP", "MA", "MD", "WT", "DA", "LK", "HP", "DP", "HV"];
    public static string PctColumn(int stat) => Stats[stat].ToLowerInvariant() + "_pct";
    public static string AddColumn(int stat) => Stats[stat].ToLowerInvariant() + "_add";

    public const int MaxPct = 100, MaxAdd = 10000;

    [JsonIgnore] public int[] Main => [ExpPct, TmPct, SpeedPct, FindPct];
    [JsonIgnore] public int[] StatPct => [ApPct, AcPct, DxPct, MpPct, MaPct, MdPct, WtPct, DaPct, LkPct, HpPct, DpPct, HvPct];
    [JsonIgnore] public int[] StatAdd => [ApAdd, AcAdd, DxAdd, MpAdd, MaAdd, MdAdd, WtAdd, DaAdd, LkAdd, HpAdd, DpAdd, HvAdd];

    public VipPackage WithEffects(int[] main, int[] pct, int[] add) => this with
    {
        ExpPct = main[0], TmPct = main[1], SpeedPct = main[2], FindPct = main[3],
        ApPct = pct[0], AcPct = pct[1], DxPct = pct[2], MpPct = pct[3], MaPct = pct[4], MdPct = pct[5],
        WtPct = pct[6], DaPct = pct[7], LkPct = pct[8], HpPct = pct[9], DpPct = pct[10], HvPct = pct[11],
        ApAdd = add[0], AcAdd = add[1], DxAdd = add[2], MpAdd = add[3], MaAdd = add[4], MdAdd = add[5],
        WtAdd = add[6], DaAdd = add[7], LkAdd = add[8], HpAdd = add[9], DpAdd = add[10], HvAdd = add[11],
    };
}

/// <summary>uspw_cash_select: the account and its points.</summary>
public sealed record Account(int AccountCode, string AccountGid, int Cash);

/// <summary>uspw_vip_char_list: a live character of the account and its VIP package (none = nulls).</summary>
public sealed record VipChar(int CharUid, string CharName, int CharLevel, int Job, int? PackageId, string? PackageName,
    DateTime? EndDate)
{
    public bool IsActive(DateTime now) => PackageId is not null && EndDate is DateTime end && end > now;
}

/// <summary>uspw_vip_history / uspw_vip_log: one purchase. Kind = new / extend / replace (ReplacedPackage = package_id the
/// replace ended). AccountCode / AccountGid only in the admin log.</summary>
public sealed record VipLog(int LogId, DateTime LogDate, int? AccountCode, string? AccountGid, int CharUid, string CharName,
    int PackageId, string PackageName, int Price, int DurationHours, string Kind, int? ReplacedPackage, DateTime? EndDate,
    int CashLeft);

/// <summary>uspw_vip_buy: RETURN code (<see cref="BuyCodes"/>) and the OUTPUT values (set when Code = 0).</summary>
public sealed record BuyResult(int Code, int CashLeft, DateTime? EndDate);

/// <summary>uspw_vip_package_delete RETURN codes.</summary>
public enum DeleteResult { Deleted = 0, Disabled = 1, NotFound = 2 }
