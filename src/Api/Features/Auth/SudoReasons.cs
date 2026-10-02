namespace Tesria.Api.Features.Auth;

/// <summary>
/// What "Confirm It's You" says each action is (t2-017). Each one is true of
/// the action that asks, rather than one sentence for all of them.
/// </summary>
public static class SudoReasons
{
    public const string Default =
        "You signed in a while ago, and this change needs your password again.";
    public const string Branding =
        "This changes how Tesria looks to everyone, so it needs your password again.";
    public const string MailSignIn =
        "Connecting a mail account changes how Tesria sends email, so it needs your password again.";
    public const string Unblock =
        "This lets a blocked address back in, so it needs your password again.";
    public const string BackupPolicy =
        "This changes how long backups are kept, so it needs your password again.";
    public const string PublicReading =
        "This changes what people who are not signed in can read, so it needs your password again.";
    public const string ResetLink =
        "A reset link lets whoever holds it into that account, so it needs your password again.";
    public const string Role =
        "This changes who can administer the instance, so it needs your password again.";
    public const string Ownership =
        "This hands the instance to someone else, so it needs your password again.";
    public const string TwoFactorOff =
        "This turns off someone's two-factor sign-in, so it needs your password again.";
    public const string Roles =
        "This changes what a role may do, so it needs your password again.";
    /// <summary>Widening what everyone signed in may do in a space (dev-plan 21.1).</summary>
    public const string OpenSpace =
        "This lets everyone signed in into more of this space at once, so it needs your password again.";
    /// <summary>Choosing who is in Global Viewers or Global Reviewers (dev-plan 21.1).</summary>
    public const string GlobalReaders =
        "Everyone in this group can read every space, so changing who is in it needs your password again.";
    /// <summary>An invite that makes its account an administrator (dev-plan 21.3).</summary>
    public const string InviteAdmin =
        "This invite makes an administrator, so it needs your password again.";
    /// <summary>An invite that puts its account in Global Viewers or Global Reviewers (dev-plan 21.3).</summary>
    public const string InviteGlobalReaders =
        "This invite puts someone in a group that can read every space, so it needs your password again.";
    public const string Purge =
        "Deleting for good cannot be undone, so it needs your password again.";
}
