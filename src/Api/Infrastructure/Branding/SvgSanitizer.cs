using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Tesria.Api.Infrastructure.Branding;

public sealed class SvgRejectedException(string message) : Exception(message);

/// <summary>
/// Makes an uploaded SVG safe to keep (dev-plan 13.1, decision 6).
///
/// <para><b>This is the first of two defences, not the only one.</b> The
/// second is that a branding SVG is only ever displayed through
/// <c>&lt;img&gt;</c> and served with a sandboxing CSP, where it cannot run
/// script or fetch anything whatever it contains. So a gap in this class is a
/// bug, not a hole. It still has to be tight, because an exported HTML file
/// opened from disk has no CSP, and because the favicon renderer reads
/// what this produces.</para>
///
/// <para><b>An allowlist, rebuilt from the parse.</b> The output is written
/// from the parsed tree, never passed through, so an SVG that is also valid
/// HTML, a comment hiding markup, or an encoding trick does not survive:
/// only elements and attributes named below, with values checked, come out
/// the other side. Anything that cannot be parsed as XML is refused rather
/// than "cleaned as far as possible".</para>
/// </summary>
public static partial class SvgSanitizer
{
    public const int MaxBytes = 256 * 1024;
    private const int MaxElements = 10_000;

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon",
        "text", "tspan", "defs", "linearGradient", "radialGradient", "stop",
        "clipPath", "mask", "use", "symbol", "title", "desc",
    };

    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "id", "class", "viewBox", "width", "height", "x", "y", "x1", "y1", "x2", "y2",
        "cx", "cy", "r", "rx", "ry", "fx", "fy", "d", "points", "transform", "version",
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-linecap",
        "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset",
        "stroke-opacity", "opacity", "clip-path", "clip-rule", "mask", "maskUnits",
        "maskContentUnits", "clipPathUnits", "gradientUnits", "gradientTransform",
        "spreadMethod", "offset", "stop-color", "stop-opacity", "font-family", "font-size",
        "font-weight", "font-style", "text-anchor", "dominant-baseline", "letter-spacing",
        "dx", "dy", "preserveAspectRatio", "style", "href", "color", "display", "visibility",
        "vector-effect", "shape-rendering", "paint-order",
    };

    /// <summary>
    /// The sanitised document and its intrinsic size (from the viewBox, or
    /// width and height), which the page needs to lay a logo out before it
    /// has loaded.
    /// </summary>
    public sealed record Result(string Svg, double Width, double Height);

    public static Result Sanitize(byte[] bytes)
    {
        if (bytes.Length > MaxBytes)
            throw new SvgRejectedException($"An SVG must be {MaxBytes / 1024} KB or smaller.");

        XDocument doc;
        try
        {
            // DTDs off (which is also what stops entity expansion bombs and
            // external entities), no resolver, so nothing is ever fetched.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxBytes * 4L,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
            };
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            doc = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            throw new SvgRejectedException("That file is not a valid SVG.");
        }

        var root = doc.Root;
        if (root is null || root.Name != Svg + "svg")
            throw new SvgRejectedException("That file is not an SVG image.");
        if (doc.Descendants().Count() > MaxElements)
            throw new SvgRejectedException("That SVG is too complex to use as a logo.");

        var clean = Rebuild(root)!;
        clean.SetAttributeValue(XNamespace.Xmlns + "xlink", XLink.NamespaceName);

        var (w, h) = Size(clean);
        if (w <= 0 || h <= 0)
            throw new SvgRejectedException("That SVG has no size. Give it a viewBox, or a width and a height.");

        var text = clean.ToString(SaveOptions.DisableFormatting);
        return new Result(text, w, h);
    }

    private static XElement? Rebuild(XElement source)
    {
        if (source.Name.Namespace != Svg || !Elements.Contains(source.Name.LocalName)) return null;

        var copy = new XElement(Svg + source.Name.LocalName);
        foreach (var attr in source.Attributes())
        {
            if (attr.IsNamespaceDeclaration) continue;
            var local = attr.Name.LocalName;
            var ns = attr.Name.Namespace;

            // xlink:href and href both mean "link"; only an internal #fragment
            // is allowed, which is what <use> and gradients need.
            if (local == "href" && (ns == XNamespace.None || ns == XLink))
            {
                if (attr.Value.StartsWith('#') && FragmentId().IsMatch(attr.Value))
                    copy.SetAttributeValue(XLink + "href", attr.Value);
                continue;
            }
            if (ns != XNamespace.None && !(ns == XNamespace.Xml && local == "space")) continue;
            if (!Attributes.Contains(local)) continue;
            if (!SafeValue(attr.Value)) continue;
            if (local == "style")
            {
                if (SafeStyle(attr.Value) is { } style) copy.SetAttributeValue(local, style);
                continue;
            }
            copy.SetAttributeValue(attr.Name, attr.Value);
        }

        foreach (var node in source.Nodes())
        {
            switch (node)
            {
                case XElement child when Rebuild(child) is { } kept:
                    copy.Add(kept);
                    break;
                case XText text when source.Name.LocalName is "text" or "tspan" or "title" or "desc":
                    copy.Add(new XText(text.Value));
                    break;
            }
        }
        return copy;
    }

    /// <summary>
    /// No script-like value anywhere, and no url() except one that points at
    /// something in this same document.
    /// </summary>
    private static bool SafeValue(string value)
    {
        var v = value.ToLowerInvariant();
        if (v.Contains("javascript:") || v.Contains("data:") || v.Contains("expression(")
            || v.Contains('\\') || v.Contains('<')) return false;
        foreach (Match m in Url().Matches(value))
            if (!m.Groups["target"].Value.Trim().Trim('"', '\'').StartsWith('#')) return false;
        return true;
    }

    /// <summary>A style attribute keeps only declarations that pass the value rules, and never an at-rule.</summary>
    private static string? SafeStyle(string style)
    {
        if (style.Contains('@')) return null;
        var kept = style.Split(';')
            .Select(d => d.Trim())
            .Where(d => d.Length > 0 && d.Contains(':') && SafeValue(d))
            .ToList();
        return kept.Count == 0 ? null : string.Join(';', kept);
    }

    private static (double W, double H) Size(XElement svg)
    {
        if (svg.Attribute("viewBox")?.Value is { } vb)
        {
            var parts = vb.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vw)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vh))
                return (vw, vh);
        }
        return (Length(svg.Attribute("width")?.Value), Length(svg.Attribute("height")?.Value));
    }

    private static double Length(string? value)
    {
        if (value is null) return 0;
        var number = LeadingNumber().Match(value);
        return number.Success && double.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    /// <summary>Whether bytes look like SVG rather than a raster image. Sniffed, never trusted from the client.</summary>
    public static bool LooksLikeSvg(byte[] bytes)
    {
        var probe = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024))
            .TrimStart('﻿', ' ', '\t', '\r', '\n');
        return probe.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            || probe.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || probe.StartsWith("<!--", StringComparison.Ordinal)
            || probe.StartsWith("<!doctype svg", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"url\s*\((?<target>[^)]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"^#[A-Za-z_][\w.\-:]*$")]
    private static partial Regex FragmentId();

    [GeneratedRegex(@"^\s*[0-9]*\.?[0-9]+")]
    private static partial Regex LeadingNumber();
}
