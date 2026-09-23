using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Videos shown as animations in an exported page (dev-plan 10.5 step 2).
/// The application's component does not survive a capture, so the one
/// script an export keeps is what makes the pause button work and holds an
/// animation still for a reader who asked for reduced motion. Losing either
/// from that script would leave exported animations with a dead button, or
/// moving for someone who asked them not to, with nothing else failing.
/// </summary>
public class AnimationExportTests
{
    [Fact]
    public void The_kept_script_drives_animations_from_their_data_attributes()
    {
        var script = SiteChrome.ThemeScript();
        Assert.Contains("data-export-keep", script);
        Assert.Contains("[data-animation]", script);
        Assert.Contains("[data-animation-toggle]", script);
        Assert.Contains("prefers-reduced-motion: reduce", script);
        // Muted is what lets a browser start a video by itself.
        Assert.Contains("v.muted = true", script);
    }
}
