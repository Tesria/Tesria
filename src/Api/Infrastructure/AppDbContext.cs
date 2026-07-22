using ConfluenceClone.Api.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Infrastructure;

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
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<PageVersion> PageVersions => Set<PageVersion>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(320);
            e.Property(u => u.DisplayName).HasMaxLength(200);
            e.Property(u => u.OidcSubject).HasMaxLength(400);
            // Emails are stored lower-cased by the app; unique across the instance.
            e.HasIndex(u => u.Email).IsUnique();
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
