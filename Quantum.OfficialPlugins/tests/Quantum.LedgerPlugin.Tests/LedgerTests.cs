using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Quantum.OfficialPlugins.Ledger;
using Quantum.OfficialPlugins.Ledger.UI.Localization;
using System.Globalization;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class LedgerTests
{
    private static LedgerEntry Entry(decimal amount = 25.50m, EntryKind kind = EntryKind.Expense,
        string category = "餐饮", string note = "") =>
        LedgerEntry.Create(Guid.NewGuid(), new(2026, 10, 18), kind, amount, category, note);

    [Fact]
    public void FenAndTotalsAreExact()
    {
        var entries = new[] { Entry(0.1m), Entry(0.2m), Entry(1m, EntryKind.Income) };
        Assert.Equal(10, entries[0].AmountInFen);
        var total = LedgerTotals.From(entries);
        Assert.Equal(0.3m, total.Expense);
        Assert.Equal(1m, total.Income);
        Assert.Equal(0.7m, total.Balance);
        Assert.Equal(new()
        {
            Income = 0,
            Expense = 0
        }, LedgerTotals.From([]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("1000000000")]
    public void InvalidMoneyIsRejectedWithoutChangingEntry(string amount)
    {
        var item = Entry();
        Assert.Throws<NOF.Domain.DomainException>(() => item.Update(new(2026, 1, 1), EntryKind.Income,
            decimal.Parse(amount, CultureInfo.InvariantCulture), "Salary", "Changed"));
        Assert.Equal(25.50m, item.Amount);
        Assert.Equal("餐饮", item.Category);
        Assert.Equal(EntryKind.Expense, item.Kind);
        Assert.Equal(new(2026, 10, 18), item.Date);
    }

    [Fact]
    public void MaximumAmountAndLeapDateAreExact()
    {
        var item = LedgerEntry.Create(Guid.NewGuid(), new(2024, 2, 29), EntryKind.Income, 999999999.99m, "工资", "");
        Assert.Equal(99999999999L, item.AmountInFen);
        Assert.Equal(999999999.99m, item.Amount);
        Assert.Equal(new(2024, 2, 29), item.Date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\nb")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void InvalidCategoryIsRejected(string category) => Assert.Throws<NOF.Domain.DomainException>(() => Entry(category: category));

    [Fact]
    public void InvalidDateKindIdAndLongNoteAreRejected()
    {
        Assert.Throws<NOF.Domain.DomainException>(() => LedgerEntry.Create(Guid.Empty, new(2026, 1, 1), EntryKind.Income, 1, "X", ""));
        Assert.Throws<NOF.Domain.DomainException>(() => LedgerEntry.Create(Guid.NewGuid(), new(1899, 1, 1), EntryKind.Income, 1, "X", ""));
        Assert.Throws<NOF.Domain.DomainException>(() => Entry(kind: (EntryKind)99));
        Assert.Throws<NOF.Domain.DomainException>(() => Entry(note: new string('x', 1001)));
    }

    [Fact]
    public void LinksAreCanonicalAndRejectSelfOrEmptyIds()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var forward = LedgerEntryLink.Create(first, second);
        var reverse = LedgerEntryLink.Create(second, first);
        Assert.Equal(forward.LeftEntryId, reverse.LeftEntryId);
        Assert.Equal(forward.RightEntryId, reverse.RightEntryId);
        Assert.Throws<NOF.Domain.DomainException>(() => LedgerEntryLink.Create(first, first));
        Assert.Throws<NOF.Domain.DomainException>(() => LedgerEntryLink.Create(first, Guid.Empty));
    }

    [Fact]
    public void ChineseAndEnglishResourcesResolveFromTheCompiledAssembly()
    {
        var services = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
        var localizer = services.GetRequiredService<IStringLocalizer<PluginStrings>>();
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Assert.Equal("记账", localizer["Ledger"].Value);
            Assert.False(localizer["Save"].ResourceNotFound);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Ledger", localizer["Ledger"].Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
            services.Dispose();
        }
    }
}
