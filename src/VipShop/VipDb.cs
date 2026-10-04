using System.Data;
using Microsoft.Data.SqlClient;

namespace VipShop;

/// <summary>
/// Everything the site asks the database. One method = one stored procedure in gmg_account (TrickMod sql/vip and
/// sql/cashshop). Prices, points, ownership and end times are decided by the procedures; the site only passes values.
/// The tests replace this with an in-memory fake.
/// </summary>
public interface IVipDb
{
    /// <summary>uspw_cash_auth: account_code of account id + login key (<see cref="LoginKey"/>), 0 = refused.</summary>
    Task<int> AuthAsync(string accountGid, string loginKey);

    /// <summary>uspw_cash_select: account id and points, null if the account does not exist.</summary>
    Task<Account?> AccountAsync(int accountCode);

    /// <summary>uspw_vip_package_list: enabled packages (all = also disabled ones, admin).</summary>
    Task<List<VipPackage>> PackagesAsync(bool all);

    /// <summary>uspw_vip_package_save: PackageId 0 = new. RETURN 0 ok, 1 values refused, 2 package not found.</summary>
    Task<(int Code, int PackageId)> SavePackageAsync(VipPackage p);

    /// <summary>uspw_vip_package_delete: deleted, or disabled when purchases exist.</summary>
    Task<DeleteResult> DeletePackageAsync(int packageId);

    /// <summary>uspw_vip_char_list: live characters of the account with their VIP package.</summary>
    Task<List<VipChar>> CharactersAsync(int accountCode);

    /// <summary>uspw_vip_buy: debit points and start / extend / replace the package of one character in one transaction.</summary>
    Task<BuyResult> BuyAsync(int accountCode, int charUid, int packageId, int expectedPrice);

    /// <summary>uspw_vip_history: purchases of one account, newest first.</summary>
    Task<List<VipLog>> HistoryAsync(int accountCode, int rows);

    /// <summary>uspw_vip_log: purchases of every account, newest first (admin).</summary>
    Task<List<VipLog>> LogAsync(int rows);
}

/// <summary>SQL Server implementation. Result columns are read by name, so their order in the procedures is free.</summary>
public sealed class SqlVipDb(string connectionString) : IVipDb
{
    private async Task<SqlConnection> OpenAsync()
    {
        var c = new SqlConnection(connectionString);
        await c.OpenAsync();
        return c;
    }

    private static SqlCommand Proc(SqlConnection c, string name)
    {
        var cmd = new SqlCommand(name, c) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@return", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue });
        return cmd;
    }

    private static SqlParameter Out(SqlCommand cmd, string name, SqlDbType type)
    {
        var p = cmd.Parameters.Add(name, type);
        p.Direction = ParameterDirection.Output;
        return p;
    }

    private static int Return(SqlCommand cmd) => cmd.Parameters["@return"].Value is int v ? v : -1;

    public async Task<int> AuthAsync(string accountGid, string loginKey)
    {
        await using var c = await OpenAsync();
        await using var cmd = Proc(c, "dbo.uspw_cash_auth");
        cmd.Parameters.Add("@account_gid", SqlDbType.VarChar, 50).Value = accountGid;
        cmd.Parameters.Add("@login_key", SqlDbType.VarChar, 24).Value = loginKey;
        var code = Out(cmd, "@account_code", SqlDbType.Int);
        await cmd.ExecuteNonQueryAsync();
        return code.Value is int v ? v : 0;
    }

    public async Task<Account?> AccountAsync(int accountCode)
    {
        await using var c = await OpenAsync();
        await using var cmd = Proc(c, "dbo.uspw_cash_select");
        cmd.Parameters.Add("@account_code", SqlDbType.Int).Value = accountCode;
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new Account(Int(r, "account_code"), Str(r, "account_gid"), Int(r, "cash"));
    }

    public async Task<List<VipPackage>> PackagesAsync(bool all)
    {
        var list = new List<VipPackage>();
        await using var c = await OpenAsync();
        await using var cmd = Proc(c, "dbo.uspw_vip_package_list");
        cmd.Parameters.Add("@all", SqlDbType.Bit).Value = all;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var p = new VipPackage
            {
                PackageId = Int(r, "package_id"),
                Name = Str(r, "name"),
                Description = Str(r, "description"),
                Price = Int(r, "price"),
                DurationHours = Int(r, "duration_hours"),
                Enabled = Convert.ToBoolean(r["enabled"]),
                Sort = Int(r, "sort"),
                UpdateDate = r["update_date"] is DateTime d ? d : null,
            };
            list.Add(p.WithEffects(
                VipPackage.MainColumns.Select(col => Int(r, col)).ToArray(),
                Enumerable.Range(0, 12).Select(i => Int(r, VipPackage.PctColumn(i))).ToArray(),
                Enumerable.Range(0, 12).Select(i => Int(r, VipPackage.AddColumn(i))).ToArray()));
        }
        return list;
    }

    public async Task<(int Code, int PackageId)> SavePackageAsync(VipPackage p)
    {
        await using var c = await OpenAsync();
        await using var cmd = Proc(c, "dbo.uspw_vip_package_save");
        var id = cmd.Parameters.Add("@package_id", SqlDbType.Int);
        id.Direction = ParameterDirection.InputOutput;
        id.Value = p.PackageId;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 50).Value = p.Name;
        cmd.Parameters.Add("@description", SqlDbType.NVarChar, 200).Value = p.Description;
        cmd.Parameters.Add("@price", SqlDbType.Int).Value = p.Price;
        cmd.Parameters.Add("@duration_hours", SqlDbType.Int).Value = p.DurationHours;
        var main = p.Main;
        for (int i = 0; i < main.Length; i++)
            cmd.Parameters.Add("@" + VipPackage.MainColumns[i], SqlDbType.Int).Value = main[i];
        var pct = p.StatPct;
        var add = p.StatAdd;
        for (int i = 0; i < 12; i++)
        {
            cmd.Parameters.Add("@" + VipPackage.PctColumn(i), SqlDbType.Int).Value = pct[i];
            cmd.Parameters.Add("@" + VipPackage.AddColumn(i), SqlDbType.Int).Value = add[i];
        }
        cmd.Parameters.Add("@enabled", SqlDbType.Bit).Value = p.Enabled;
        cmd.Parameters.Add("@sort", SqlDbType.Int).Value = p.Sort;
        await cmd.ExecuteNonQueryAsync();
        return (Return(cmd), id.Value is int v ? v : 0);
    }

    public async Task<DeleteResult> DeletePackageAsync(int packageId)
    {
        await using var c = await OpenAsync();
        await using var cmd = Proc(c, "dbo.uspw_vip_package_delete");
        cmd.Parameters.Add("@package_id", SqlDbType.Int).Value = packageId;
        await cmd.ExecuteNonQueryAsync();
        return (DeleteResult)Return(cmd);
    }

    public async Task<List<VipChar>> CharactersAsync(int accountCode)
    {
        var list = new List<VipChar>();
        await using var c = await OpenAsync();
        await using var cmd = Proc(c, "dbo.uspw_vip_char_list");
        cmd.Parameters.Add("@account_code", SqlDbType.Int).Value = accountCode;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new VipChar(Int(r, "char_uid"), Str(r, "char_name"), Int(r, "char_level"), Int(r, "job"),
                IntOrNull(r, "package_id"), r["package_name"] as string, r["end_date"] as DateTime?));
        return list;
    }

    public async Task<BuyResult> BuyAsync(int accountCode, int charUid, int packageId, int expectedPrice)
    {
        await using var c = await OpenAsync();
        await using var cmd = Proc(c, "dbo.uspw_vip_buy");
        cmd.Parameters.Add("@account_code", SqlDbType.Int).Value = accountCode;
        cmd.Parameters.Add("@char_uid", SqlDbType.Int).Value = charUid;
        cmd.Parameters.Add("@package_id", SqlDbType.Int).Value = packageId;
        cmd.Parameters.Add("@expected_price", SqlDbType.Int).Value = expectedPrice;
        var cash = Out(cmd, "@cash_left", SqlDbType.Int);
        var end = Out(cmd, "@end_date", SqlDbType.DateTime);
        await cmd.ExecuteNonQueryAsync();
        return new BuyResult(Return(cmd), cash.Value is int v ? v : 0, end.Value as DateTime?);
    }

    public Task<List<VipLog>> HistoryAsync(int accountCode, int rows) => LogRowsAsync("dbo.uspw_vip_history", accountCode, rows);

    public Task<List<VipLog>> LogAsync(int rows) => LogRowsAsync("dbo.uspw_vip_log", null, rows);

    private async Task<List<VipLog>> LogRowsAsync(string proc, int? accountCode, int rows)
    {
        var list = new List<VipLog>();
        await using var c = await OpenAsync();
        await using var cmd = Proc(c, proc);
        if (accountCode is int code) cmd.Parameters.Add("@account_code", SqlDbType.Int).Value = code;
        cmd.Parameters.Add("@rows", SqlDbType.Int).Value = rows;
        await using var r = await cmd.ExecuteReaderAsync();
        bool admin = Enumerable.Range(0, r.FieldCount).Any(i => r.GetName(i) == "account_gid");
        while (await r.ReadAsync())
            list.Add(new VipLog(Int(r, "log_id"), (DateTime)r["log_date"], admin ? IntOrNull(r, "account_code") : null,
                admin ? r["account_gid"] as string : null, Int(r, "char_uid"), Str(r, "char_name"), Int(r, "package_id"),
                Str(r, "package_name"), Int(r, "price"), Int(r, "duration_hours"), Str(r, "kind"),
                IntOrNull(r, "replaced_package"), r["end_date"] as DateTime?, Int(r, "cash_left")));
        return list;
    }

    private static int Int(SqlDataReader r, string col) => r[col] is DBNull ? 0 : Convert.ToInt32(r[col]);
    private static int? IntOrNull(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToInt32(r[col]);
    private static string Str(SqlDataReader r, string col) => r[col] is string s ? s.TrimEnd() : "";
}
