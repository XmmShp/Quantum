using NOF.Infrastructure;
using Quantum.ExampleCalendarPlugin.Domain;

namespace Quantum.ExampleCalendarPlugin.Infrastructure;

internal sealed class CalendarDbContextModelCreatingContributor
    : IDbContextModelCreatingContributor
{
    public void Configure(IDbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CalendarItem>(entity =>
        {
            entity.ToTable("CalendarPluginItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Date).IsRequired();
            entity.Property(item => item.StartTime).IsRequired();
            entity.Property(item => item.Style).HasMaxLength(32).IsRequired();
            entity.Property(item => item.CreatedAt).IsRequired();
            entity.Property(item => item.UpdatedAt).IsRequired();
            entity.HasIndex(item => new { item.Date, item.StartTime });
        });
    }
}
