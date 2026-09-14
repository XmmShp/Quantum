using NOF.Infrastructure;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Infrastructure;

internal sealed class CalendarDbContextModelCreatingContributor : IDbContextModelCreatingContributor
{
    public void Configure(IDbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CalendarEntry>(entity =>
        {
            entity.ToTable("OfficialCalendarEntries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Notes).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Kind).IsRequired();
            entity.Property(item => item.Date).IsRequired();
            entity.Property(item => item.StartTime).IsRequired();
            entity.Property(item => item.Style).HasMaxLength(16).IsRequired();
            entity.Property(item => item.IsCompleted).IsRequired();
            entity.Property(item => item.CreatedAt).IsRequired();
            entity.Property(item => item.UpdatedAt).IsRequired();
            entity.HasIndex(item => new { item.Date, item.StartTime });
            entity.HasIndex(item => new { item.Kind, item.IsCompleted, item.Date });
        });
    }
}
