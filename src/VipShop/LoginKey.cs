using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VipShop;

/// <summary>
/// The login key of a password, exactly as the game launcher (TrickModLaunch.exe) makes it and gmg_account.dbo.uspw_cash_auth
/// checks it (TrickMod docs/login/README.md, sql/cashshop/10_cashshop.sql): bytes 4..11 of
/// MD5(LOWER(RTRIM(first 18 characters of the password))) read big-endian as a signed 64-bit number, written in decimal.
/// The same 8 bytes as 16 lowercase hex digits are tbl_account.account_pw (dbo.fn_Md5_Encrypt). Passwords are ASCII 32..126
/// (fn_AccountPW_ValidCheck); anything else cannot match and gives null.
/// </summary>
public static class LoginKey
{
    /// <summary>FirstLoginSvr's @account_pw is varchar(18): longer passwords are cut there.</summary>
    public const int MaxPassword = 18;

    public static string? Of(string password) =>
        Bytes(password) is { } b ? BinaryPrimitives.ReadInt64BigEndian(b).ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>The value stored in tbl_account.account_pw (tests compare with known rows).</summary>
    public static string? Hex(string password) => Bytes(password) is { } b ? Convert.ToHexStringLower(b) : null;

    private static byte[]? Bytes(string password)
    {
        if (password.Length > MaxPassword) password = password[..MaxPassword];
        password = password.TrimEnd(' ');
        if (password.Length == 0) return null;
        var ascii = new byte[password.Length];
        for (int i = 0; i < password.Length; i++)
        {
            char ch = password[i];
            if (ch < 32 || ch > 126) return null;
            ascii[i] = (byte)(ch is >= 'A' and <= 'Z' ? ch + 32 : ch);
        }
        return MD5.HashData(ascii)[4..12];
    }
}
