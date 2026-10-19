using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

internal static class LedgerQueries
{
    public static IQueryable<LedgerEntry> Month(IRepository<LedgerEntry> entries, DateOnly month)
    {
        if (month.Year is < 1900 or > 9998)
        {
            throw new ArgumentException("Date must be between 1900 and 9998.");
        }

        var start = new DateOnly(month.Year, month.Month, 1);
        var end = start.AddMonths(1);
        return entries.AsNoTracking().Where(item => item.Date >= start && item.Date < end)
            .OrderByDescending(item => item.Date).ThenBy(item => item.Id);
    }

    public static async Task<LedgerEntryDetails[]> DetailsAsync(LedgerEntry[] entries,
        IRepository<LedgerEntryLink> links, CancellationToken cancellationToken)
    {
        var ids = entries.Select(item => item.Id).ToArray();
        var related = await links.AsNoTracking().Where(link => ids.Contains(link.LeftEntryId) || ids.Contains(link.RightEntryId))
            .ToArrayAsync(cancellationToken);
        return entries.Select(item => LedgerEntryDetails.From(item, related
            .Where(link => link.LeftEntryId == item.Id || link.RightEntryId == item.Id)
            .Select(link => link.LeftEntryId == item.Id ? link.RightEntryId : link.LeftEntryId)
            .Distinct().Order().ToArray())).ToArray();
    }

    public static async Task ReplaceLinksAsync(IRepository<LedgerEntry> entries,
        IRepository<LedgerEntryLink> links, Guid id, Guid[]? relatedIds, CancellationToken cancellationToken)
    {
        var targets = (relatedIds ?? []).Distinct().ToArray();
        if (targets.Any(target => target == Guid.Empty || target == id))
        {
            throw new ArgumentException("An entry cannot link to itself or an empty ID.");
        }

        var found = await entries.AsNoTracking().Where(item => targets.Contains(item.Id)).ToArrayAsync(cancellationToken);
        if (found.Length != targets.Length)
        {
            throw new KeyNotFoundException("Related entry does not exist.");
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
