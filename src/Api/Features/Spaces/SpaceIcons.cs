using System.Globalization;
using System.Text;
using Tesria.Api.Domain;

namespace Tesria.Api.Features.Spaces;

/// <summary>
/// Validation for a space's chosen icon (dev-plan 6).
///
/// The emoji case is the interesting one. "Is this an emoji" has no exact
/// answer worth chasing: the set changes with every Unicode release, and a
/// server that tries to enumerate it will be wrong within a year. What
/// matters is that the value is a *glyph*, not prose and not markup, because
/// it is rendered inline wherever the space appears. So the rule is shaped
/// around that: short, no control characters, and at least one non-ASCII
/// character. That admits keycaps (which really do contain an ASCII digit)
/// and any future emoji, and refuses "hello" and anything script-shaped.
/// </summary>
public static class SpaceIcons
{
    /// <summary>
    /// UTF-16 length cap. A family ZWJ sequence with skin tones runs to about
    /// eleven code units; sixteen leaves room without admitting a sentence.
    /// </summary>
    public const int MaxEmojiLength = 16;

    /// <summary>Sanity bound on the palette index; the palette itself lives in the client.</summary>
    public const int MaxColorIndex = 63;

    /// <summary>
    /// Normalises and checks an emoji. Returns the value to store, or an
    /// error message written to be shown to the person who typed it.
    /// </summary>
    public static (string? Value, string? Error) NormalizeEmoji(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Length == 0) return (null, "Pick an emoji.");
        if (value.Length > MaxEmojiLength) return (null, "That is too long for an icon: pick a single emoji.");

        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
                return (null, "That is not a valid icon.");
        }

        var hasNonAscii = false;
        foreach (var rune in value.EnumerateRunes())
            if (rune.Value > 0x7F) { hasNonAscii = true; break; }

        return hasNonAscii ? (value, null) : (null, "Pick an emoji rather than letters.");
    }

    public static string? ValidateColor(int? color) =>
        color is { } c && (c < 0 || c > MaxColorIndex) ? "Not a valid colour." : null;

    /// <summary>
    /// Returns the space to its generated icon, deleting a stored image if
    /// that is what it had: otherwise switching from a picture to an emoji
    /// would leave the bytes behind forever.
    /// </summary>
    public static void Clear(Space space, Infrastructure.Storage.IProfileMediaService media)
    {
        if (space.IconKind == SpaceIconKind.Image)
            media.Delete(media.KeyFor(Infrastructure.Storage.ProfileMediaKind.SpaceIcon, space.Id));
        space.IconKind = SpaceIconKind.None;
        space.IconValue = null;
    }
}
