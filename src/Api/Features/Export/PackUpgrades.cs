using System.Text.Json.Nodes;

namespace Tesria.Api.Features.Export;

/// <summary>
/// A pack as JSON, before it is read into <see cref="WikiPack.Model"/>, for
/// the upgrade steps to change (dev-plan 16.3). Everything is mutable; a step
/// edits it in place.
/// </summary>
public sealed class PackDocument(JsonObject manifest, JsonObject space, JsonObject? authors, IReadOnlyList<JsonObject> pages)
{
    public JsonObject Manifest { get; } = manifest;
    public JsonObject Space { get; } = space;
    public JsonObject? Authors { get; } = authors;
    public IReadOnlyList<JsonObject> Pages { get; } = pages;

    /// <summary>
    /// Every editor document in the pack, for a step that changes the shape of
    /// a node (a renamed attribute, a split node): each version of every page,
    /// and each template. <paramref name="upgrade"/> returns the document as the
    /// new format has it.
    /// </summary>
    public void ForEachDocument(Func<JsonNode, JsonNode> upgrade)
    {
        foreach (var page in Pages)
            if (page["versions"] is JsonArray versions)
                foreach (var version in versions.OfType<JsonObject>())
                    if (version["content"] is { } content)
                        version["content"] = upgrade(content.DeepClone());
        if (Space["templates"] is JsonArray templates)
            foreach (var template in templates.OfType<JsonObject>())
                if (template["content"]?.GetValue<string>() is { } text && JsonNode.Parse(text) is { } doc)
                    template["content"] = upgrade(doc).ToJsonString();
    }
}

/// <summary>One step from a format to the next.</summary>
/// <param name="From">The format it reads; it leaves the pack at <c>From + 1</c>.</param>
/// <param name="Description">What changed, for the error if the step fails.</param>
public sealed record PackUpgrade(int From, string Description, Action<PackDocument> Apply);

/// <summary>
/// The compatibility promise (dev-plan 16.3): a pack made by any release from
/// 0.5 on imports into every later release. When a release has to raise
/// <see cref="WikiPack.Format"/>, it adds the step from the previous format
/// here, the way a database change adds a migration, and commits a pack made
/// by the release before it under <c>tests/Api.Tests/Packs</c>, which the
/// tests import. Steps run in order, so the import only ever reads the
/// current format. A change to an editor node's shape is a step too (using
/// <see cref="PackDocument.ForEachDocument"/>), with a matching database
/// migration for the pages already stored.
///
/// Empty today: format 1 is the only format there has been.
/// </summary>
public static class PackUpgrades
{
    public static readonly IReadOnlyList<PackUpgrade> All = [];

    /// <summary>
    /// Brings a pack from <paramref name="from"/> to <paramref name="to"/>,
    /// or says which step is missing or failed.
    /// </summary>
    public static void Apply(PackDocument pack, int from, int to, IReadOnlyList<PackUpgrade> steps)
    {
        for (var format = from; format < to; format++)
        {
            var step = steps.FirstOrDefault(s => s.From == format)
                ?? throw new WikiPack.PackException($"This Tesria has no way to upgrade a pack from format {format}. Import it with an older Tesria, export it again, and import that.");
            try { step.Apply(pack); }
            catch (Exception ex) when (ex is not WikiPack.PackException)
            {
                throw new WikiPack.PackException($"Upgrading this pack from format {format} to {format + 1} ({step.Description}) failed: {ex.Message}");
            }
            pack.Manifest["format"] = format + 1;
        }
    }
}
