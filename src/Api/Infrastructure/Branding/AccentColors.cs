using System.Globalization;
using System.Text.RegularExpressions;

namespace Tesria.Api.Infrastructure.Branding;

/// <summary>The six color tokens one accent sets for one mode (see the accent block in <c>index.css</c>).</summary>
public sealed record AccentTokens(
    string Primary, string PrimaryDark, string PrimarySoft, string PrimarySofter,
    string PrimarySoftBorder, string OnPrimary);

/// <summary>How readable an accent is in one mode, by the WCAG AA text rule (4.5:1).</summary>
public sealed record AccentCheck(
    string Mode, string Color, AccentTokens Tokens,
    double PrimaryVsBackground, double OnPrimaryVsPrimary, bool Passes, string? Suggested);

/// <summary>
/// A custom accent (dev-plan 13.1, decisions 4 and 5): one color per mode
/// from the owner, the other five tokens derived here.
///
/// <para><b>Why derived and not asked for.</b> The six built-in accents were
/// each tuned by hand, twelve colors at a time, and checked for contrast.
/// Nobody configuring a brand wants to pick a hover shade and three tints;
/// they have one color. The derivation works in OKLCH, where "the same hue,
/// lighter" is a straight line, which is not true of HSL. The shapes of the
/// built-in pairs set the offsets.</para>
///
/// <para><b>Why the check exists, and why it does not refuse.</b>
/// <c>--primary</c> is link text, so it needs 4.5:1 against the page, and
/// the text on a filled button needs 4.5:1 against <c>--primary</c>. A color
/// that fails gets the nearest shade that passes suggested beside it. The
/// suggestion is an offer, not a rule (decided 2026-09-22): a better shade
/// is suggested, and an administrator may keep their own.</para>
/// </summary>
public static partial class AccentColors
{
    public const double Required = 4.5;

    /// <summary>The page background each mode's accent is read against.</summary>
    public const string LightBackground = "#ffffff";
    public const string DarkBackground = "#161a1d";

    /// <summary>The two inks text on a filled accent can be.</summary>
    private const string White = "#ffffff";
    private const string Ink = "#1d2125";

    /// <summary>
    /// <c>#rgb</c> or <c>#rrggbb</c>, any case, with or without the hash, to
    /// lower-case <c>#rrggbb</c>; null for anything else. Nothing that is not
    /// exactly a color ever reaches a stylesheet.
    /// </summary>
    public static string? Normalize(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (!v.StartsWith('#')) v = "#" + v;
        if (!HexColor().IsMatch(v)) return null;
        if (v.Length == 4) v = $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}";
        return v.ToLowerInvariant();
    }

    /// <summary>The tokens for <paramref name="hex"/> in the given mode.</summary>
    public static AccentTokens Derive(string hex, bool dark)
    {
        var (l, c, h) = ToOklch(Parse(hex));
        string At(double lightness, double chroma) => ToHex(FromOklch(Math.Clamp(lightness, 0, 1), chroma, h));

        var primary = Normalize(hex)!;
        return dark
            ? new AccentTokens(
                primary,
                At(l + 0.08, c),
                At(0.31, Math.Min(c * 0.30, 0.05)),
                At(0.27, Math.Min(c * 0.20, 0.035)),
                At(0.40, Math.Min(c * 0.45, 0.07)),
                OnPrimary(primary, preferWhite: false))
            : new AccentTokens(
                primary,
                At(l - 0.08, c * 0.95),
                At(0.935, Math.Min(c * 0.25, 0.04)),
                At(0.965, Math.Min(c * 0.12, 0.02)),
                At(0.85, Math.Min(c * 0.45, 0.08)),
                OnPrimary(primary, preferWhite: true));
    }

    /// <summary>The check the admin page shows beside a color, with a better shade when it fails.</summary>
    public static AccentCheck Check(string hex, bool dark)
    {
        var tokens = Derive(hex, dark);
        var bg = dark ? DarkBackground : LightBackground;
        var vsBg = Contrast(tokens.Primary, bg);
        var onPrimary = Contrast(tokens.OnPrimary, tokens.Primary);
        var passes = vsBg >= Required && onPrimary >= Required;
        return new AccentCheck(
            dark ? "dark" : "light", tokens.Primary, tokens,
            Math.Round(vsBg, 2), Math.Round(onPrimary, 2), passes,
            passes ? null : Suggest(tokens.Primary, dark));
    }

    /// <summary>
    /// The nearest shade that passes: the same hue, lightness moved away from
    /// the background a little at a time until both checks pass. Null only if
    /// no shade of that hue can, which does not happen for a real color.
    /// </summary>
    public static string? Suggest(string hex, bool dark)
    {
        var (l, c, h) = ToOklch(Parse(hex));
        var bg = dark ? DarkBackground : LightBackground;
        for (var step = 1; step <= 200; step++)
        {
            var lightness = dark ? l + step * 0.005 : l - step * 0.005;
            if (lightness is < 0 or > 1) break;
            var candidate = ToHex(FromOklch(lightness, c, h));
            if (Contrast(candidate, bg) >= Required
                && Contrast(OnPrimary(candidate, preferWhite: !dark), candidate) >= Required)
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// The stylesheet for a custom accent, emitted into the page's head as
    /// <c>data-accent="brand"</c>, in the same shape as the built-in blocks:
    /// a light block, then the dark one twice (by media query, and by the
    /// explicit dark theme), so the specificity rules the comment in
    /// <c>index.css</c> describes hold for it too.
    /// </summary>
    public static string Stylesheet(string? light, string? dark)
    {
        light = Normalize(light);
        dark = Normalize(dark);
        if (light is null && dark is null) return "";
        var baseColor = light ?? dark!;
        var lightTokens = Derive(baseColor, dark: light is null);
        var darkTokens = dark is null ? null : Derive(dark, dark: true);

        static string Block(AccentTokens t) =>
            $"--primary:{t.Primary};--primary-dark:{t.PrimaryDark};--primary-soft:{t.PrimarySoft};"
            + $"--primary-softer:{t.PrimarySofter};--primary-soft-border:{t.PrimarySoftBorder};--on-primary:{t.OnPrimary};";

        var css = $":root{{--accent-dot-brand:{lightTokens.Primary};}}"
            + $":root[data-accent=\"brand\"]{{{Block(lightTokens)}}}";
        if (darkTokens is not null)
            css += "@media (prefers-color-scheme: dark){"
                + $":root:not([data-theme=\"light\"]){{--accent-dot-brand:{darkTokens.Primary};}}"
                + $":root:not([data-theme=\"light\"])[data-accent=\"brand\"]{{{Block(darkTokens)}}}}}"
                + $":root[data-theme=\"dark\"]{{--accent-dot-brand:{darkTokens.Primary};}}"
                + $":root[data-theme=\"dark\"][data-accent=\"brand\"]{{{Block(darkTokens)}}}";
        return css;
    }

    /// <summary>WCAG 2 contrast ratio between two colors, 1 to 21.</summary>
    public static double Contrast(string a, string b)
    {
        var la = Luminance(Parse(a));
        var lb = Luminance(Parse(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// White or dark ink, whichever reads better on the accent. Light mode
    /// keeps white whenever white passes, as all six built-in accents do;
    /// dark mode keeps ink on the same terms.
    /// </summary>
    private static string OnPrimary(string primary, bool preferWhite)
    {
        var white = Contrast(White, primary);
        var ink = Contrast(Ink, primary);
        if (preferWhite && white >= Required) return White;
        if (!preferWhite && ink >= Required) return Ink;
        return white >= ink ? White : Ink;
    }

    /* ---- color space ---------------------------------------------------- */

    private static (double R, double G, double B) Parse(string hex)
    {
        var v = Normalize(hex) ?? throw new ArgumentException($"Not a color: {hex}", nameof(hex));
        double Channel(int i) => int.Parse(v.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber) / 255.0;
        return (Channel(0), Channel(1), Channel(2));
    }

    private static string ToHex((double R, double G, double B) rgb)
    {
        static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return $"#{Byte(rgb.R):x2}{Byte(rgb.G):x2}{Byte(rgb.B):x2}";
    }

    private static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    private static double FromLinear(double c) => c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

    private static double Luminance((double R, double G, double B) c) =>
        0.2126 * ToLinear(c.R) + 0.7152 * ToLinear(c.G) + 0.0722 * ToLinear(c.B);

    /// <summary>sRGB to OKLCH (Björn Ottosson's OKLab, in polar form).</summary>
    private static (double L, double C, double H) ToOklch((double R, double G, double B) c)
    {
        double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        var L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        var A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        var B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        return (L, Math.Sqrt(A * A + B * B), Math.Atan2(B, A));
    }

    /// <summary>
    /// OKLCH to sRGB, reducing chroma until the color exists on a screen. A
    /// light tint of a saturated hue is often outside sRGB, and clamping each
    /// channel instead would shift its hue.
    /// </summary>
    private static (double R, double G, double B) FromOklch(double L, double C, double h)
    {
        (double, double, double) Raw(double chroma)
        {
            double a = chroma * Math.Cos(h), b = chroma * Math.Sin(h);
            var l = Math.Pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
            var m = Math.Pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
            var s = Math.Pow(L - 0.0894841775 * a - 1.2914855480 * b, 3);
            return (
                4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
        }

        static bool InGamut((double R, double G, double B) c) =>
            c.R is >= -0.0005 and <= 1.0005 && c.G is >= -0.0005 and <= 1.0005 && c.B is >= -0.0005 and <= 1.0005;

        var linear = Raw(C);
        if (!InGamut(linear))
        {
            double lo = 0, hi = C;
            for (var i = 0; i < 24; i++)
            {
                var mid = (lo + hi) / 2;
                if (InGamut(Raw(mid))) lo = mid; else hi = mid;
            }
            linear = Raw(lo);
        }
        return (FromLinear(Math.Clamp(linear.Item1, 0, 1)),
                FromLinear(Math.Clamp(linear.Item2, 0, 1)),
                FromLinear(Math.Clamp(linear.Item3, 0, 1)));
    }

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();
}
