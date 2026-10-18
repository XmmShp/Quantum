using NOF.Application;
using NOF.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Quantum.OfficialPlugins.Ledger;

/// <summary>Each operation owns a scope so failed writes cannot leak tracked changes into a later save.</summary>
public sealed class LedgerService(IServiceScopeFactory scopes)
{
    public async Task<LedgerEntry[]> ListMonthAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        return await scope.ServiceProvider.GetRequiredService<LedgerSession>().ListMonthAsync(month, cancellationToken);
    }

    public async Task<LedgerEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        return await scope.ServiceProvider.GetRequiredService<LedgerSession>().GetAsync(id, cancellationToken);
    }

    public async Task<LedgerEntry> SaveAsync(Guid? id, DateOnly date, EntryKind kind, decimal amount, string category, string note, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        return await scope.ServiceProvider.GetRequiredService<LedgerSession>().SaveAsync(id, date, kind, amount, category, note, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        await scope.ServiceProvider.GetRequiredService<LedgerSession>().DeleteAsync(id, cancellationToken);
    }

    public async Task<int> ImportAsync(IReadOnlyList<LedgerEntry> imported, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        return await scope.ServiceProvider.GetRequiredService<LedgerSession>().ImportAsync(imported, cancellationToken);
    }
}

internal sealed class LedgerSession(IRepository<LedgerEntry> entries, IDbContext database)
{
    public Task<LedgerEntry[]> ListMonthAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        if (month.Year is < 1900 or > 9998) throw new ArgumentException("Date must be between 1900 and 9998.");
        var start = new DateOnly(month.Year, month.Month, 1);
        var end = start.AddMonths(1);
        return entries.AsNoTracking().Where(item => item.Date >= start && item.Date < end)
            .OrderByDescending(item => item.Date).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
    }

    public Task<LedgerEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => entries.AsNoTracking().Where(item => item.Id == id).SingleOrDefaultAsync(cancellationToken);

    public async Task<LedgerEntry> SaveAsync(Guid? id, DateOnly date, EntryKind kind, decimal amount, string category, string note, CancellationToken cancellationToken = default)
    {
        // Validate before tracking or changing any persistent entity.
        var validated = LedgerEntry.Create(id ?? Guid.NewGuid(), date, kind, amount, category, note);
        if (id is null) await entries.AddAsync(validated, cancellationToken);
        else
        {
            var item = await entries.Where(item => item.Id == id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Record no longer exists. Refresh and try again.");
            item.Update(date, kind, amount, category, note);
        }
        await database.SaveChangesAsync(cancellationToken);
        return validated;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await entries.Where(item => item.Id == id).SingleOrDefaultAsync(cancellationToken);
        if (item is null) return;
        entries.Remove(item);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ImportAsync(IReadOnlyList<LedgerEntry> imported, CancellationToken cancellationToken = default)
    {
        if (imported.Count is < 1 or > LedgerCsv.MaxRecords || imported.Select(item => item.Id).Distinct().Count() != imported.Count)
            throw new ArgumentException("CSV must contain 1–1,000 records with unique IDs.");
        // One database commit: a bad file cannot produce a partial import.
        var ids = imported.Select(item => item.Id).ToArray();
        var existing = await entries.AsNoTracking().Where(item => ids.Contains(item.Id)).ToArrayAsync(cancellationToken);
        var byId = existing.ToDictionary(item => item.Id);
        foreach (var item in imported)
            if (byId.TryGetValue(item.Id, out var previous) && !previous.SameContent(item))
                throw new ArgumentException("An imported ID conflicts with an existing record.");
        var added = 0;
        foreach (var item in imported)
        {
            if (byId.ContainsKey(item.Id)) continue;
            await entries.AddAsync(item, cancellationToken);
            added++;
        }
        if (added > 0) await database.SaveChangesAsync(cancellationToken);
        return added;
    }
}
