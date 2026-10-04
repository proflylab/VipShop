using System.Globalization;
using System.Net;
using System.Text;

namespace VipShop;

/// <summary>HTML pieces shared by the player and admin pages (normal current browsers: modern CSS, no script needed).</summary>
public static class Html
{
    public static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string N(long n) => n.ToString("#,0", CultureInfo.InvariantCulture);

    public static string Page(string title, string header, string body) =>
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + E(title) + "</title>" +
        "<link rel=\"stylesheet\" href=\"/assets/site.css\"></head><body>" + header + "<main>" + body + "</main></body></html>";

    public static string Msg(string? text, bool error = false) =>
        string.IsNullOrEmpty(text) ? "" : "<p class=\"msg" + (error ? " err" : "") + "\">" + E(text) + "</p>";

    public static string Empty(string icon, string text) => "<section class=\"card empty\">" + icon + "<p>" + E(text) + "</p></section>";

    /// <summary>Points pill of the header.</summary>
    public static string Points(int cash, string pointName) =>
        "<span class=\"pill\">" + Icon.Coin + "<b>" + N(cash) + "</b> <small>" + E(pointName) + "</small></span>";

    /// <summary>Non-zero bonuses of a package: "+20% EXP" chips, then "AP +10% +50" chips for the stats.</summary>
    public static string Effects(VipPackage p)
    {
        var sb = new StringBuilder("<ul class=\"fx\">");
        var main = p.Main;
        for (int i = 0; i < main.Length; i++)
            if (main[i] != 0)
                sb.Append("<li class=\"fx-main\"><b>+").Append(main[i]).Append("%</b> ").Append(E(VipPackage.MainLabels[i])).Append("</li>");
        var pct = p.StatPct;
        var add = p.StatAdd;
        for (int i = 0; i < 12; i++)
        {
            if (pct[i] == 0 && add[i] == 0) continue;
            sb.Append("<li class=\"fx-stat\"><b>").Append(VipPackage.Stats[i]).Append("</b>");
            if (pct[i] != 0) sb.Append(" +").Append(pct[i]).Append('%');
            if (add[i] != 0) sb.Append(" +").Append(N(add[i]));
            sb.Append("</li>");
        }
        if (sb.Length == "<ul class=\"fx\">".Length) sb.Append("<li class=\"muted\">No bonus</li>");
        return sb.Append("</ul>").ToString();
    }

    /// <summary>Current VIP of a character: package name + end date and time left, or a dash.</summary>
    public static string CharVip(VipChar c, DateTime now)
    {
        if (!c.IsActive(now)) return "<span class=\"muted\">-</span>";
        return "<span class=\"tag vip\">" + Icon.Crown + E(c.PackageName) + "</span> <span class=\"muted\">until " +
               Duration.Date(c.EndDate!.Value) + " (" + Duration.Left(c.EndDate.Value, now) + " left)</span>";
    }

    public static string Hidden(string name, string value) =>
        "<input type=\"hidden\" name=\"" + name + "\" value=\"" + E(value) + "\">";

    /// <summary>Small inline SVG icons.</summary>
    public static class Icon
    {
        private const string Svg = "<svg class=\"ic\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">";

        public const string Sparkle = Svg +
            "<path fill=\"#ffd84d\" d=\"M12 1l2.6 7.4L22 11l-7.4 2.6L12 21l-2.6-7.4L2 11l7.4-2.6z\"/>" +
            "<circle fill=\"#ff8fbf\" cx=\"20\" cy=\"4\" r=\"2\"/></svg>";

        public const string Coin = Svg +
            "<circle fill=\"#ffd84d\" stroke=\"#f2a93b\" stroke-width=\"2\" cx=\"12\" cy=\"12\" r=\"9\"/>" +
            "<path fill=\"#f2a93b\" d=\"M12 6.5l1.5 3.4 3.7.3-2.8 2.4.9 3.6L12 14.3 8.7 16.2l.9-3.6-2.8-2.4 3.7-.3z\"/></svg>";

        public const string Crown = Svg +
            "<path fill=\"#ffd84d\" stroke=\"#f2a93b\" stroke-width=\"1.2\" stroke-linejoin=\"round\" d=\"M3 8l4.5 4L12 5l4.5 7L21 8l-2 11H5z\"/>" +
            "<circle fill=\"#ff8fbf\" cx=\"12\" cy=\"14.5\" r=\"1.6\"/></svg>";

        public const string Heart = Svg +
            "<path fill=\"#ff6fae\" d=\"M12 21s-8-5.2-8-11a4.5 4.5 0 0 1 8-2.8A4.5 4.5 0 0 1 20 10c0 5.8-8 11-8 11z\"/></svg>";

        public const string Oops = Svg +
            "<circle fill=\"#ffd2e4\" cx=\"12\" cy=\"12\" r=\"10\"/><circle fill=\"#e2559a\" cx=\"9\" cy=\"10\" r=\"1.3\"/>" +
            "<circle fill=\"#e2559a\" cx=\"15\" cy=\"10\" r=\"1.3\"/>" +
            "<path fill=\"none\" stroke=\"#e2559a\" stroke-width=\"1.6\" stroke-linecap=\"round\" d=\"M8.5 16q3.5-3 7 0\"/></svg>";

        public const string Clock = Svg +
            "<circle fill=\"#c8ecff\" cx=\"12\" cy=\"12\" r=\"9\"/>" +
            "<path fill=\"none\" stroke=\"#4aa8e8\" stroke-width=\"1.8\" stroke-linecap=\"round\" d=\"M12 7v5l3 2\"/></svg>";
    }
}
