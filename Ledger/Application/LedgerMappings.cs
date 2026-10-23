using System.Linq.Expressions;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

internal static class LedgerMappings
{
    public static Expression<Func<LedgerEntry, LedgerEntryProjection>> Projection => entry => new LedgerEntryProjection
    {
        Entry = new LedgerEntryDetails
        {
            Id = entry.Id,
            Date = entry.Date,
            Kind = entry.Kind,
            Amount = entry.AmountInFen / 100m,
            Category = entry.Category,
            Note = entry.Note,
            RelatedEntryIds = new Guid[0]
        },
        CalendarEntryIdsJson = entry.CalendarEntryIdsJson
    };
}
