using NOF.Domain;
namespace Quantum.OfficialPlugins.Ledger;

/// <summary>An undirected clue between two ledger entries; it carries no financial meaning.</summary>
public sealed class LedgerEntryLink
{
    private LedgerEntryLink()
    {
    }

    public Guid Id
    {
        get; private set;
    }

    public Guid LeftEntryId
    {
        get; private set;
    }

    public Guid RightEntryId
    {
        get; private set;
    }

    public static LedgerEntryLink Create(Guid first, Guid second)
    {
        if (first == Guid.Empty || second == Guid.Empty || first == second)
        {
            throw new DomainException("ledger_invalid_request", "An entry cannot link to itself or an empty ID.");
        }

        return new LedgerEntryLink
        {
            Id = Guid.NewGuid(),
            LeftEntryId = first.CompareTo(second) < 0 ? first : second,
            RightEntryId = first.CompareTo(second) < 0 ? second : first
        };
    }
}
