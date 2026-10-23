using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

internal static class LedgerQueries
{
    public static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("ledger_invalid_request", "Invalid record ID.");
        }
    }

    public static IQueryable<LedgerEntry> Month(IRepository<LedgerEntry> entries, DateOnly month)
    {
        if (month.Year is < 1900 or > 9998)
        {
            throw new DomainException("ledger_invalid_request", "Date must be between 1900 and 9998.");
        }

        var start = new DateOnly(month.Year, month.Month, 1);
        var end = start.AddMonths(1);
        return entries.AsNoTracking().Where(item => item.Date >= start && item.Date < end)
            .OrderByDescending(item => item.Date).ThenBy(item => item.Id);
    }

    public static async Task<LedgerEntryDetails[]> DetailsAsync(IQueryable<LedgerEntry> entries,
        IRepository<LedgerEntryLink> links, CancellationToken cancellationToken)
    {
        var projected = await entries.ProjectTo<LedgerEntryProjection>().ToArrayAsync(cancellationToken);
        return await CompleteDetailsAsync(projected, links, cancellationToken);
    }

    public static Task<LedgerEntryDetails[]> DetailsAsync(LedgerEntry[] entries,
        IRepository<LedgerEntryLink> links, CancellationToken cancellationToken)
        => CompleteDetailsAsync(entries.Select(entry => Mapper.Current.Map<LedgerEntry, LedgerEntryProjection>(entry)).ToArray(), links, cancellationToken);

    private static async Task<LedgerEntryDetails[]> CompleteDetailsAsync(LedgerEntryProjection[] projected,
        IRepository<LedgerEntryLink> links, CancellationToken cancellationToken)
    {
        if (projected.Length == 0)
        {
            return [];
        }

        var ids = projected.Select(item => item.Entry.Id).ToArray();
        var related = await links.AsNoTracking().Where(link => ids.Contains(link.LeftEntryId) || ids.Contains(link.RightEntryId))
            .ToArrayAsync(cancellationToken);
        foreach (var item in projected)
        {
            item.Entry.RelatedEntryIds = related
                .Where(link => link.LeftEntryId == item.Entry.Id || link.RightEntryId == item.Entry.Id)
                .Select(link => link.LeftEntryId == item.Entry.Id ? link.RightEntryId : link.LeftEntryId)
                .Distinct().Order().ToArray();
            item.Entry.CalendarEntryIds = System.Text.Json.JsonSerializer.Deserialize<Guid[]>(item.CalendarEntryIdsJson)!;
        }

        return projected.Select(item => item.Entry).ToArray();
    }

    public static async Task ReplaceLinksAsync(IRepository<LedgerEntry> entries,
        IRepository<LedgerEntryLink> links, Guid id, Guid[]? relatedIds, CancellationToken cancellationToken)
    {
        var targets = (relatedIds ?? []).Distinct().ToArray();
        if (targets.Any(target => target == Guid.Empty || target == id))
        {
            throw new DomainException("ledger_invalid_request", "An entry cannot link to itself or an empty ID.");
        }

        var found = await entries.AsNoTracking().Where(item => targets.Contains(item.Id)).ToArrayAsync(cancellationToken);
        if (found.Length != targets.Length)
        {
            throw new DomainException("ledger_entry_not_found", "Related entry does not exist.");
        }

        var existing = await links.Where(link => link.LeftEntryId == id || link.RightEntryId == id).ToArrayAsync(cancellationToken);
        foreach (var link in existing)
        {
            var other = link.LeftEntryId == id ? link.RightEntryId : link.LeftEntryId;
            if (!targets.Contains(other))
            {
                links.Remove(link);
            }
        }

        foreach (var target in targets)
        {
            if (!existing.Any(link => link.LeftEntryId == target || link.RightEntryId == target))
            {
                await links.AddAsync(LedgerEntryLink.Create(id, target), cancellationToken);
            }
        }
    }
}
