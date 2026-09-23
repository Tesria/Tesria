using Tesria.Api.Domain;

namespace Tesria.Api.Features.Export;

/// <summary>The formats a space can be exported in, each of which can be turned off (dev-plan 12.3).</summary>
public enum ExportFormat { Markdown, Html, Pdf, Site, Pack }

/// <summary>
/// Whether a space allows an export, and how a refusal is worded.
///
/// <para>Checked by every export endpoint, whoever is asking, administrators
/// and the owner included. The people who can change the setting can turn a
/// format back on, and that change is audited; an administrator quietly
/// exporting a space the setting says cannot be exported is what the
/// setting exists to stop.</para>
///
/// <para>What this is not: a barrier to reading. Anyone who can read a page
/// can copy it, and the API and MCP server return page content to anyone
/// allowed to read it. Turning exports off removes the downloads, which is
/// the difference between "someone could copy this" and "the product hands
/// out a tidy copy of the whole space".</para>
/// </summary>
public static class SpaceExports
{
    public static bool Allows(Space space, ExportFormat format) => format switch
    {
        ExportFormat.Markdown => space.ExportMarkdown,
        ExportFormat.Html => space.ExportHtml,
        ExportFormat.Pdf => space.ExportPdf,
        ExportFormat.Site => space.ExportSite,
        ExportFormat.Pack => space.ExportPack,
        _ => false,
    };

    public static string Label(ExportFormat format) => format switch
    {
        ExportFormat.Markdown => "Markdown",
        ExportFormat.Html => "HTML",
        ExportFormat.Pdf => "PDF",
        ExportFormat.Site => "a website",
        ExportFormat.Pack => "a wiki pack",
        _ => format.ToString(),
    };

    /// <summary>
    /// 403 with a code the SPA can recognize and a sentence a person can
    /// act on. Not 404: the space exists and the reader can see it, so
    /// pretending otherwise would be a stranger answer than the truth.
    /// </summary>
    public static IResult Refused(Space space, ExportFormat format) => Results.Json(new
    {
        code = "export_disabled",
        message = $"Exporting {space.Name} as {Label(format)} is turned off for this space.",
    }, statusCode: StatusCodes.Status403Forbidden);
}
