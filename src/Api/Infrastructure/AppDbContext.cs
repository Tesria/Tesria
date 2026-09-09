using Tesria.Api.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure;

/// <summary>
/// EF Core database context for the whole application. Entity shape follows
/// PLAN §4; relationships and Postgres-specific column types (jsonb) are
/// configured in <see cref="OnModelCreating"/>. Also stores ASP.NET Data
/// Protection keys so auth cookies survive redeploys.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<PageVersion> PageVersions => Set<PageVersion>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<PageLabel> PageLabels => Set<PageLabel>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<CollabDocument> CollabDocuments => Set<CollabDocument>();
    public DbSet<PageTemplate> PageTemplates => Set<PageTemplate>();
    public DbSet<Watch> Watches => Set<Watch>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<SpacePermission> SpacePermissions => Set<SpacePermission>();
    public DbSet<PageRestriction> PageRestrictions => Set<PageRestriction>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<SiteSettings>(e =>
        {
            e.Property(x => x.InstanceName).HasMaxLength(200);
            e.Property(x => x.SmtpHost).HasMaxLength(400);
            e.Property(x => x.SmtpUsername).HasMaxLength(400);
            e.Property(x => x.SmtpFromAddress).HasMaxLength(320);
            // Data Protection payloads are base64 and grow with the key; no cap.
            e.Property(x => x.SmtpPasswordProtected);
        });

        b.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(320);
            e.Property(u => u.DisplayName).HasMaxLength(200);
            e.Property(u => u.OidcSubject).HasMaxLength(400);
            // Emails are stored lower-cased by the app; unique across the instance.
            e.HasIndex(u => u.Email).IsUnique();
            // Unique only among non-null values — many local accounts share the
            // "no external identity" null value, so a plain unique index would
            // reject the second local account outright.
            e.HasIndex(u => u.OidcSubject).IsUnique().HasFilter("\"OidcSubject\" IS NOT NULL");
        });

        b.Entity<Space>(e =>
        {
            e.Property(s => s.Key).HasMaxLength(50);
            e.Property(s => s.Name).HasMaxLength(200);
            e.HasIndex(s => s.Key).IsUnique();

            e.HasOne(s => s.CreatedBy)
                .WithMany()
                .HasForeignKey(s => s.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Homepage points into this space's own pages; never cascade from it.
            e.HasOne(s => s.Homepage)
                .WithMany()
                .HasForeignKey(s => s.HomepageId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Page>(e =>
        {
            e.Property(p => p.Title).HasMaxLength(500);

            // Trashed pages, and pages still in the invisible Draft state (created
            // but never published — see PageEndpoints.CreateDraft), are hidden from
            // all normal queries. Trash/restore and draft/publish operations opt
            // back in with IgnoreQueryFilters().
            e.HasQueryFilter(p => p.DeletedAt == null && p.Status != PageStatus.Draft);

            // Full-text search. On PostgreSQL, SearchVector is a generated
            // tsvector column over SearchText with a GIN index. Other providers
            // (SQLite, used by tests) don't support tsvector, so it's ignored
            // there and search falls back to LIKE over SearchText.
            if (Database.IsNpgsql())
            {
                e.HasGeneratedTsVectorColumn(p => p.SearchVector!, "english", p => p.SearchText)
                    .HasIndex(p => p.SearchVector!)
                    .HasMethod("GIN");
            }
            else
            {
                e.Ignore(p => p.SearchVector);
            }

            e.HasOne(p => p.Space)
                .WithMany(s => s.Pages)
                .HasForeignKey(p => p.SpaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Self-referencing tree. Deleting a parent is handled in application
            // code (re-parent or cascade explicitly), so restrict at the DB level.
            e.HasOne(p => p.ParentPage)
                .WithMany(p => p.Children)
                .HasForeignKey(p => p.ParentPageId)
                .OnDelete(DeleteBehavior.Restrict);

            // The "current version" pointer. The versions themselves cascade from
            // the page (below); this pointer must not add a second cascade path.
            e.HasOne(p => p.CurrentVersion)
                .WithMany()
                .HasForeignKey(p => p.CurrentVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(p => new { p.SpaceId, p.ParentPageId });
        });

        b.Entity<PageVersion>(e =>
        {
            e.Property(v => v.ContentJson).HasColumnType("jsonb");
            e.Property(v => v.ChangeComment).HasMaxLength(500);

            e.HasOne(v => v.Page)
                .WithMany(p => p.Versions)
                .HasForeignKey(v => v.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(v => v.Author)
                .WithMany()
                .HasForeignKey(v => v.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(v => new { v.PageId, v.VersionNumber }).IsUnique();
        });

        b.Entity<Attachment>(e =>
        {
            e.Property(a => a.Filename).HasMaxLength(500);
            e.Property(a => a.ContentType).HasMaxLength(200);
            e.Property(a => a.StorageKey).HasMaxLength(500);

            e.HasOne(a => a.Page)
                .WithMany()
                .HasForeignKey(a => a.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.UploadedBy)
                .WithMany()
                .HasForeignKey(a => a.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(a => a.PageId);
        });

        b.Entity<ApiToken>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.TokenHash).HasMaxLength(200);
            e.Property(t => t.Prefix).HasMaxLength(20);

            e.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(t => t.TokenHash).IsUnique();
        });

        b.Entity<Webhook>(e =>
        {
            e.Property(w => w.Url).HasMaxLength(2000);
            e.Property(w => w.Secret).HasMaxLength(200);
            e.Property(w => w.Events).HasMaxLength(500);

            e.HasOne(w => w.Space)
                .WithMany()
                .HasForeignKey(w => w.SpaceId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(w => w.CreatedBy)
                .WithMany()
                .HasForeignKey(w => w.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(w => w.SpaceId);
        });

        b.Entity<Watch>(e =>
        {
            e.HasKey(w => new { w.UserId, w.TargetType, w.TargetId });
            e.Property(w => w.TargetType).HasMaxLength(50);

            e.HasOne(w => w.User)
                .WithMany()
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(w => new { w.TargetType, w.TargetId });
        });

        b.Entity<Notification>(e =>
        {
            e.Property(n => n.Action).HasMaxLength(100);
            e.Property(n => n.TargetType).HasMaxLength(50);
            e.Property(n => n.MetadataJson).HasColumnType("jsonb");

            e.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(n => n.Actor)
                .WithMany()
                .HasForeignKey(n => n.ActorId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(n => new { n.UserId, n.CreatedAt });
        });

        b.Entity<PageTemplate>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.Description).HasMaxLength(500);
            e.Property(t => t.ContentJson).HasColumnType("jsonb");

            e.HasOne(t => t.Space)
                .WithMany()
                .HasForeignKey(t => t.SpaceId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(t => t.SpaceId);
        });

        b.Entity<CollabDocument>(e =>
        {
            // Written and read by the collaboration sidecar, keyed by page id.
            e.HasKey(d => d.DocumentName);
            e.Property(d => d.DocumentName).HasMaxLength(200);
        });

        b.Entity<SpacePermission>(e =>
        {
            e.HasOne(p => p.Space)
                .WithMany()
                .HasForeignKey(p => p.SpaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // One row per (space, principal, operation).
            e.HasIndex(p => new { p.SpaceId, p.PrincipalType, p.PrincipalId, p.Operation }).IsUnique();
        });

        b.Entity<PageRestriction>(e =>
        {
            e.HasOne(r => r.Page)
                .WithMany()
                .HasForeignKey(r => r.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(r => new { r.PageId, r.PrincipalType, r.PrincipalId, r.Operation }).IsUnique();
        });

        b.Entity<Group>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(200);
            e.Property(g => g.NormalizedName).HasMaxLength(200);
            e.Property(g => g.Description).HasMaxLength(500);
            e.HasIndex(g => g.NormalizedName).IsUnique();
        });

        b.Entity<UserGroup>(e =>
        {
            e.HasKey(ug => new { ug.UserId, ug.GroupId });

            e.HasOne(ug => ug.User)
                .WithMany()
                .HasForeignKey(ug => ug.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ug => ug.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(ug => ug.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ug => ug.GroupId);
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(a => a.Action).HasMaxLength(100);
            e.Property(a => a.TargetType).HasMaxLength(50);
            e.Property(a => a.MetadataJson).HasColumnType("jsonb");

            e.HasOne(a => a.Actor)
                .WithMany()
                .HasForeignKey(a => a.ActorId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => new { a.TargetType, a.TargetId });
        });

        b.Entity<Label>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(50);
            // Names are stored lower-cased by the app; unique across the instance.
            e.HasIndex(l => l.Name).IsUnique();
        });

        b.Entity<PageLabel>(e =>
        {
            e.HasKey(pl => new { pl.PageId, pl.LabelId });

            e.HasOne(pl => pl.Page)
                .WithMany()
                .HasForeignKey(pl => pl.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(pl => pl.Label)
                .WithMany(l => l.PageLabels)
                .HasForeignKey(pl => pl.LabelId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(pl => pl.LabelId);
        });

        b.Entity<Comment>(e =>
        {
            e.Property(c => c.AnchorJson).HasColumnType("jsonb");

            e.HasOne(c => c.Page)
                .WithMany()
                .HasForeignKey(c => c.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.ParentComment)
                .WithMany(c => c.Replies)
                .HasForeignKey(c => c.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(c => c.PageId);
        });
    }
}
