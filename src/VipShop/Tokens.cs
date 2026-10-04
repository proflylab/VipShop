using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VipShop;

/// <summary>A logged-in player: cookie of the pages and Bearer token of the API.</summary>
public sealed record PlayerSession(int AccountCode, string AccountGid);

/// <summary>What the confirm page of a purchase allows its form to buy (once).</summary>
public sealed record BuyTicket(int AccountCode, int CharUid, int PackageId, int Price);

/// <summary>
/// HMAC-signed tokens (no server-side session store): "player|expiry|account_code|account_gid", "admin|expiry" and the
/// one-time "buy|expiry|account|char|package|price|nonce" of a confirm form (a double click or a reloaded result page
/// does not buy twice). No password or login key is ever put in a token.
/// </summary>
public sealed class Tokens
{
    private readonly byte[] _key;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, long> _used = new();

    public Tokens(string secret, TimeProvider? time = null)
    {
        _key = string.IsNullOrEmpty(secret) ? RandomNumberGenerator.GetBytes(32) : SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        _time = time ?? TimeProvider.System;
    }

    public string IssuePlayer(PlayerSession s, TimeSpan life) =>
        Sign(string.Join('|', "player", Expiry(life), s.AccountCode.ToString(CultureInfo.InvariantCulture), s.AccountGid));

    public PlayerSession? Player(string? token)
    {
        var f = Open(token, "player", 4);
        return f is not null && int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out var code) && code > 0
            ? new PlayerSession(code, f[3]) : null;
    }

    public string IssueAdmin(TimeSpan life) => Sign("admin|" + Expiry(life));

    public bool Admin(string? token) => Open(token, "admin", 2) is not null;

    public string IssueBuy(BuyTicket t, TimeSpan life) => Sign(string.Join('|', "buy", Expiry(life),
        t.AccountCode.ToString(CultureInfo.InvariantCulture), t.CharUid.ToString(CultureInfo.InvariantCulture),
        t.PackageId.ToString(CultureInfo.InvariantCulture), t.Price.ToString(CultureInfo.InvariantCulture),
        Convert.ToHexString(RandomNumberGenerator.GetBytes(12))));

    /// <summary>The ticket of a valid confirm form of this account; null if forged, expired, of another account or
    /// already used.</summary>
    public BuyTicket? RedeemBuy(string? token, int accountCode)
    {
        var f = Open(token, "buy", 7);
        if (f is null) return null;
        var inv = CultureInfo.InvariantCulture;
        if (!int.TryParse(f[2], NumberStyles.None, inv, out var account) || account != accountCode ||
            !int.TryParse(f[3], NumberStyles.None, inv, out var ch) || !int.TryParse(f[4], NumberStyles.None, inv, out var pkg) ||
            !int.TryParse(f[5], NumberStyles.None, inv, out var price)) return null;
        Sweep();
        if (!_used.TryAdd(f[6], long.Parse(f[1], inv))) return null;
        return new BuyTicket(account, ch, pkg, price);
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeSeconds();
    private string Expiry(TimeSpan life) => (Now() + (long)life.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    /// <summary>Fields of a valid, unexpired token of this kind (the last field may contain '|').</summary>
    private string[]? Open(string? token, string kind, int fields)
    {
        var body = Verify(token);
        if (body is null) return null;
        var f = body.Split('|', fields);
        return f.Length == fields && f[0] == kind &&
               long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var exp) && exp >= Now() ? f : null;
    }

    private string Sign(string body)
    {
        var data = Encoding.UTF8.GetBytes(body);
        return B64(data) + "." + B64(HMACSHA256.HashData(_key, data));
    }

    private string? Verify(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 512) return null;
        int dot = token.IndexOf('.');
        if (dot <= 0) return null;
        try
        {
            var data = UnB64(token[..dot]);
            var mac = UnB64(token[(dot + 1)..]);
            return CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(_key, data)) ? Encoding.UTF8.GetString(data) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Forget used confirm forms once they have expired anyway.</summary>
    private void Sweep()
    {
        if (_used.Count < 1024) return;
        long now = Now();
        foreach (var kv in _used)
            if (kv.Value < now) _used.TryRemove(kv.Key, out _);
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] UnB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
