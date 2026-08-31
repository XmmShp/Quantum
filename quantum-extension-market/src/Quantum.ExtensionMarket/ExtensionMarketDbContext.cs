using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NOF.Infrastructure.EntityFrameworkCore;
using Quantum.ExtensionMarket.Domain;

namespace Quantum.ExtensionMarket.Infrastructure;

public sealed class ExtensionMarketDbContext(DbContextOptions<ExtensionMarketDbContext> options)
    : NOFDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MarketUser>(entity =>
        {
            entity.ToTable(nameof(MarketUser));
            entity.IsHostOnly();
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).ValueGeneratedNever();
            entity.Property(user => user.Username).HasMaxLength(64).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.Roles).HasConversion<int>().IsRequired();
            entity.Property(user => user.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(user => user.LastLoginAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(user => user.Username).IsUnique();
            entity.HasIndex(user => user.Email).IsUnique();
        });

        modelBuilder.Entity<PluginListing>(entity =>
        {
            entity.ToTable(nameof(PluginListing));
            entity.IsHostOnly();
            entity.HasKey(listing => listing.Id);
            entity.Property(listing => listing.Id).ValueGeneratedNever();
            entity.Property(listing => listing.PluginId).HasMaxLength(200).IsRequired();
            entity.Property(listing => listing.Name).HasMaxLength(200).IsRequired();
            entity.Property(listing => listing.Description).HasMaxLength(4000).IsRequired();
            entity.Property(listing => listing.AuthorUserId).IsRequired();
            entity.Property(listing => listing.Tags).HasColumnType("text[]").IsRequired();
            entity.Property(listing => listing.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(listing => listing.UpdatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(listing => listing.PluginId).IsUnique();
            entity.HasIndex(listing => new { listing.AuthorUserId, listing.UpdatedAtUtc });
        });

        modelBuilder.Entity<PluginRelease>(entity =>
        {
            entity.ToTable(nameof(PluginRelease));
            entity.IsHostOnly();
            entity.HasKey(release => release.Id);
            entity.Property(release => release.Id).ValueGeneratedNever();
            entity.Property(release => release.ListingId).IsRequired();
            entity.Property(release => release.Version).HasMaxLength(100).IsRequired();
            entity.Property(release => release.QuantumVersionSupport).HasMaxLength(200).IsRequired();
            entity.Property(release => release.ReleaseNotes).HasMaxLength(8000).IsRequired();
            entity.Property(release => release.PackagePath).HasMaxLength(1000).IsRequired();
            entity.Property(release => release.PackageSha256).HasMaxLength(64).IsRequired();
            entity.Property(release => release.Status).HasConversion<short>().IsRequired();
            entity.Property(release => release.UploadedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(release => release.ReviewedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(release => release.ReviewNotes).HasMaxLength(2000);
            entity.HasIndex(release => new { release.ListingId, release.Version }).IsUnique();
            entity.HasIndex(release => new { release.Status, release.UploadedAtUtc });
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable(nameof(AuditEntry));
            entity.IsHostOnly();
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).ValueGeneratedNever();
            entity.Property(entry => entry.Action).HasMaxLength(100).IsRequired();
            entity.Property(entry => entry.Details).HasMaxLength(4000).IsRequired();
            entity.Property(entry => entry.OccurredAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(entry => entry.OccurredAtUtc);
            entity.HasIndex(entry => new { entry.ActorUserId, entry.OccurredAtUtc });
            entity.HasIndex(entry => new { entry.ListingId, entry.OccurredAtUtc });
        });
    }
}
