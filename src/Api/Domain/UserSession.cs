namespace Tesria.Api.Domain;

/// <summary>
/// One signed-in browser (dev-plan 3.5). The cookie carries this row's id;
/// the row is what makes an individual cookie revocable without signing out
/// every other device the way rotating <see cref="User.SecurityStamp"/> does.
/// Rows are kept after revocation so "signed out on <date>" can be shown.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
