using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Branding;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Administration → Branding (dev-plan 13.1).
///
/// Everything here is behind <c>settings.branding</c>, which only the owner
/// holds until they grant it. The instance name is not here: the owner
/// decided that the instance name and the branding are "two separate
/// entities", so renaming the instance stays in Settings with the rights it
/// always had, and changes nothing anyone sees in the header.
///
/// Every save is sudo, like the retention policy: the branding is what every
/// person, signed in or not, sees first.
/// </summary>
public static partial class BrandingEndpoints
{
    public const int MaxNameLength = 60;

    public record BrandingSettings(
        string? BrandName, string Display, string SignInArrangement,
        string ThemePolicy, string AccentPolicy, string? AccentName,
        string? AccentLight, string? AccentDark,
        BrandLogo? Logo, BrandLogo? LogoDark, string? FaviconHash, bool FaviconHasSvg,
        bool IsCustomized, DateTimeOffset? ChangedAt, string? ChangedByName,
        List<AccentCheck> Checks);

    public record BrandingRequest(
        string? BrandName, string? Display, string? SignInArrangement,
        string? ThemePolicy, string? AccentPolicy, string? AccentName,
        string? AccentLight, string? AccentDark);

    public record PreviewRequest(string? Light, string? Dark);

    public static RouteGroupBuilder MapBrandingEndpoints(this RouteGroupBuilder api)
    {
        // The right is required on the group, not route by route: the file
        // routes were added later in their own file, and a per-route
        // requirement is one that gets forgotten on the next route. It was,
        // briefly, and the route-rights test caught it.
        var group = api.MapGroup("/admin/branding").WithTags("Admin").RequireAuthorization()
            .RequirePermission(InstancePermissions.SettingsBranding);
        group.MapGet("", Get);
        group.MapPut("", Update);
        group.MapPost("/accent-preview", Preview);
        group.MapPost("/reset", Reset);
        group.MapBrandingFileEndpoints();
        return api;
    }

    private static async Task<IResult> Get(ISiteSettingsService settings, AppDbContext db) =>
        Results.Ok(await ViewAsync(await settings.GetAsync(), db));

    /// <summary>
    /// Saves everything on the tab except the files, which have their own
    /// endpoints. The whole form each time: a partial update would make "I
    /// cleared the brand name" and "I did not send the brand name" the same.
    /// </summary>
    private static async Task<IResult> Update(
        BrandingRequest req, ISiteSettingsService settings, AppDbContext db, CurrentUser current,
        IAuditLogger audit, HttpContext http, IConfiguration config)
    {
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;

        var errors = new Dictionary<string, string[]>();
        var name = req.BrandName?.Trim();
        if (string.IsNullOrEmpty(name)) name = null;
        if (name is { Length: > MaxNameLength })
            errors["brandName"] = [$"Keep the brand name to {MaxNameLength} characters."];
        if (name is not null && name.Any(char.IsControl))
            errors["brandName"] = ["The brand name cannot contain line breaks or control characters."];

        var display = req.Display ?? BrandView.DisplayBoth;
        if (!BrandView.Displays.Contains(display)) errors["display"] = ["Choose logo and name, logo only, or name only."];
        var arrangement = req.SignInArrangement ?? BrandView.SideBySide;
        if (!BrandView.Arrangements.Contains(arrangement)) errors["signInArrangement"] = ["Choose side by side or stacked."];
        var theme = req.ThemePolicy ?? BrandView.Any;
        if (!BrandView.ThemePolicies.Contains(theme)) errors["themePolicy"] = ["Choose any, light only or dark only."];
        var accentPolicy = req.AccentPolicy ?? BrandView.Any;
        if (!BrandView.AccentPolicies.Contains(accentPolicy)) errors["accentPolicy"] = ["Choose whether people may pick their own accent."];

        var accentName = string.IsNullOrWhiteSpace(req.AccentName) ? null : req.AccentName.Trim();
        if (accentName is not null && accentName != BrandView.BrandAccent && !BrandView.BuiltInAccents.Contains(accentName))
            errors["accentName"] = ["That is not one of the accents."];

        // Colours arrive as text and are stored only as normalised #rrggbb.
        // Anything else is refused rather than guessed at: this value is
        // written into a stylesheet on every page (decision 4).
        string? light = null, dark = null;
        if (!string.IsNullOrWhiteSpace(req.AccentLight))
        {
            light = AccentColors.Normalize(req.AccentLight);
            if (light is null) errors["accentLight"] = ["Enter a colour as #rrggbb."];
        }
        if (!string.IsNullOrWhiteSpace(req.AccentDark))
        {
            dark = AccentColors.Normalize(req.AccentDark);
            if (dark is null) errors["accentDark"] = ["Enter a colour as #rrggbb."];
        }

        // A custom accent needs a colour for each mode people can be in.
        if (accentName == BrandView.BrandAccent && !errors.ContainsKey("accentLight") && !errors.ContainsKey("accentDark"))
        {
            if (theme != BrandView.Dark && light is null)
                errors["accentLight"] = ["A custom accent needs a light-mode colour while the light theme is allowed."];
            if (theme != BrandView.Light && dark is null)
                errors["accentDark"] = ["A custom accent needs a dark-mode colour while the dark theme is allowed."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var before = BrandView.From(await settings.GetAsync());
        var actorId = current.RequireId();

        // The owner decided a colour that fails the contrast check may still
        // be kept (decision C). It is recorded, so "why are the links hard to
        // read" has an answer in the audit log.
        var overridden = new List<string>();
        if (light is not null && !AccentColors.Check(light, dark: false).Passes) overridden.Add("light");
        if (dark is not null && !AccentColors.Check(dark, dark: true).Passes) overridden.Add("dark");

        var saved = await settings.UpdateAsync(s =>
        {
            s.BrandName = name;
            s.BrandDisplay = display;
            s.SignInArrangement = arrangement;
            s.ThemePolicy = theme;
            s.AccentPolicy = accentPolicy;
            s.AccentName = accentName;
            s.BrandAccentLight = light;
            s.BrandAccentDark = dark;
            s.BrandChangedAt = DateTimeOffset.UtcNow;
            s.BrandChangedById = actorId;
        }, actorId);

        audit.Record("branding.changed", "instance", null, new
        {
            Before = Summary(before),
            After = Summary(BrandView.From(saved)),
            ContrastOverridden = overridden.Count > 0 ? overridden : null,
        });
        await db.SaveChangesAsync();

        return Results.Ok(await ViewAsync(saved, db));
    }

    /// <summary>
    /// What a colour would become: the derived tokens, the two contrast
    /// ratios per mode, and a shade that passes when it does not. Nothing is
    /// saved; the tab calls this as someone types.
    /// </summary>
    private static IResult Preview(PreviewRequest req)
    {
        var checks = new List<AccentCheck>();
        var errors = new Dictionary<string, string[]>();
        if (!string.IsNullOrWhiteSpace(req.Light))
        {
            if (AccentColors.Normalize(req.Light) is { } l) checks.Add(AccentColors.Check(l, dark: false));
            else errors["light"] = ["Enter a colour as #rrggbb."];
        }
        if (!string.IsNullOrWhiteSpace(req.Dark))
        {
            if (AccentColors.Normalize(req.Dark) is { } d) checks.Add(AccentColors.Check(d, dark: true));
            else errors["dark"] = ["Enter a colour as #rrggbb."];
        }
        return errors.Count > 0 ? Results.ValidationProblem(errors) : Results.Ok(checks);
    }

    /// <summary>
    /// Reset to Tesria (decision 14): the brand name, every uploaded file,
    /// the colours and the locks. The instance name is untouched, because it
    /// was never branding.
    /// </summary>
    private static async Task<IResult> Reset(
        ISiteSettingsService settings, AppDbContext db, CurrentUser current, IAuditLogger audit,
        IBrandAssets assets, HttpContext http, IConfiguration config)
    {
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;

        var before = BrandView.From(await settings.GetAsync());
        var actorId = current.RequireId();

        var saved = await settings.UpdateAsync(s =>
        {
            s.BrandName = null;
            s.BrandDisplay = BrandView.DisplayBoth;
            s.SignInArrangement = BrandView.SideBySide;
            s.BrandLogoHash = null; s.BrandLogoFormat = null; s.BrandLogoWidth = null; s.BrandLogoHeight = null;
            s.BrandLogoDarkHash = null; s.BrandLogoDarkFormat = null; s.BrandLogoDarkWidth = null; s.BrandLogoDarkHeight = null;
            s.BrandFaviconHash = null; s.BrandFaviconHasSvg = false;
            s.ThemePolicy = BrandView.Any;
            s.AccentPolicy = BrandView.Any;
            s.AccentName = null;
            s.BrandAccentLight = null;
            s.BrandAccentDark = null;
            s.BrandChangedAt = DateTimeOffset.UtcNow;
            s.BrandChangedById = actorId;
        }, actorId);

        // After the settings commit: a file left behind is harmless (nothing
        // points at it), a setting pointing at a deleted file is a broken image.
        assets.DeleteAll();

        audit.Record("branding.reset", "instance", null, new { Before = Summary(before) });
        await db.SaveChangesAsync();
        return Results.Ok(await ViewAsync(saved, db));
    }

    internal static async Task<BrandingSettings> ViewAsync(SiteSettings s, AppDbContext db)
    {
        var b = BrandView.From(s);
        string? by = null;
        if (s.BrandChangedById is { } id)
            by = (await db.Users.FindAsync(id))?.DisplayName;

        var checks = new List<AccentCheck>();
        if (b.AccentLight is { } l) checks.Add(AccentColors.Check(l, dark: false));
        if (b.AccentDark is { } d) checks.Add(AccentColors.Check(d, dark: true));

        // The stored preference, not the effective one: "logo only" is shown
        // as chosen even while there is no logo yet to show alone.
        return new BrandingSettings(
            s.BrandName, BrandView.Displays.Contains(s.BrandDisplay) ? s.BrandDisplay : b.Display,
            b.SignInArrangement, b.ThemePolicy, b.AccentPolicy, s.AccentName,
            b.AccentLight, b.AccentDark, b.Logo, b.LogoDark, b.FaviconHash, b.FaviconHasSvg,
            b.IsCustomized, s.BrandChangedAt, by, checks);
    }

    /// <summary>What an audit entry records about the branding: values and file hashes, never file contents.</summary>
    internal static object Summary(BrandView b) => new
    {
        b.Name, b.HasCustomName, b.Display, b.SignInArrangement,
        Logo = b.Logo?.Url, LogoDark = b.LogoDark?.Url, Favicon = b.FaviconHash,
        b.ThemePolicy, b.AccentPolicy, b.AccentName, b.AccentLight, b.AccentDark,
    };
}
