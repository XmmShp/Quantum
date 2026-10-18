using NOF.Infrastructure;

namespace Quantum.OfficialPlugins.Ledger;

internal sealed class LedgerModel : IDbContextModelCreatingContributor
{
    public void Configure(IDbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.ToTable("OfficialLedgerEntries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Date).IsRequired();
            entity.Property(item => item.Kind).IsRequired();
            entity.Property(item => item.AmountInFen).IsRequired();
            entity.Property(item => item.Category).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Note).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.RelatedExpenseId);
            entity.HasIndex(item => item.RelatedExpenseId);
            entity.HasIndex(item => item.Date);
        });
    }
}
