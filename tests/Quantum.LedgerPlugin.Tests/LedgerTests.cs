using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Quantum.OfficialPlugins.Ledger;
using Quantum.OfficialPlugins.Ledger.Localization;
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
        Assert.Equal(new(0, 0), LedgerTotals.From([]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("1000000000")]
    public void InvalidMoneyIsRejectedWithoutChangingEntry(string amount)
    {
        var item = Entry();
        Assert.Throws<ArgumentException>(() => item.Update(new(2026, 1, 1), EntryKind.Income,
            decimal.Parse(amount, CultureInfo.InvariantCulture), "Salary", "Changed"));
        Assert.Equal(25.50m, item.Amount);
        Assert.Equal("餐饮", item.Category);
        Assert.Equal(EntryKind.Expense, item.Kind);
        Assert.Equal(new(2026, 10, 18), item.Date);
    }

    [Fact]
    public void MaximumAmountAndLeapDateRoundTrip()
    {
        var item = LedgerEntry.Create(Guid.NewGuid(), new(2024, 2, 29), EntryKind.Income, 999999999.99m, "工资", "");
        Assert.Equal(99999999999L, item.AmountInFen);
        Assert.True(item.SameContent(Assert.Single(LedgerCsv.Parse(LedgerCsv.Export([item])))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\nb")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void InvalidCategoryIsRejected(string category) => Assert.Throws<ArgumentException>(() => Entry(category: category));

    [Fact]
    public void InvalidDateKindIdAndLongNoteAreRejected()
    {
        Assert.Throws<ArgumentException>(() => LedgerEntry.Create(Guid.Empty, new(2026, 1, 1), EntryKind.Income, 1, "X", ""));
        Assert.Throws<ArgumentException>(() => LedgerEntry.Create(Guid.NewGuid(), new(1899, 1, 1), EntryKind.Income, 1, "X", ""));
        Assert.Throws<ArgumentException>(() => Entry(kind: (EntryKind)99));
        Assert.Throws<ArgumentException>(() => Entry(note: new string('x', 1001)));
    }

    [Theory]
    [InlineData("Lunch, tea\r\n\"quoted\"")]
    [InlineData("=HYPERLINK(\"https://example.invalid\")")]
    [InlineData("+123")]
    [InlineData("-123")]
    [InlineData("@SUM(1,2)")]
    [InlineData("\tformula")]
    [InlineData("'literal")]
    [InlineData("'=literal")]
    [InlineData("")]
    public void CsvRoundTripsUnicodeQuotesNewlinesAndFormulaPrefixes(string note)
    {
        var item = Entry(note: note);
        var result = Assert.Single(LedgerCsv.Parse(LedgerCsv.Export([item])));
        Assert.Equal(item.Id, result.Id);
        Assert.True(item.SameContent(result));
        if (note.StartsWith('='))
        {
            Assert.Contains("'=", LedgerCsv.Export([item]));
        }
    }

    [Fact]
    public void CsvUsesInvariantDatesAndAmountsAcrossCultures()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var csv = LedgerCsv.Export([Entry(1234.56m)]);
            Assert.Contains("2026-10-18", csv);
            Assert.Contains("1234.56", csv);
            Assert.Equal(1234.56m, Assert.Single(LedgerCsv.Parse(csv)).Amount);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void DuplicateIdsAndMalformedCsvAreRejected()
    {
        var item = Entry();
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse(LedgerCsv.Export([item, item])));
        var csv = LedgerCsv.Export([item]);
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse(csv.Replace("expense", "unknown")));
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse(csv.Replace("25.50", "25.501")));
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse(csv.Replace("2026-10-18", "2026-02-30")));
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse("id,date,type,amount,category,note\r\n\"unclosed"));
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse("id,date,type,amount,category,note\r\n\"closed\"x,1"));
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse("id,date,type,amount,category,note\r\n"));
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse(new string('x', 2 * 1024 * 1024 + 1)));
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse(LedgerCsv.Export(Enumerable.Range(0, 1001).Select(_ => Entry()))));
    }

    [Fact]
    public void LegacyCsvRemainsReadableAndRelationsRoundTrip()
    {
        var id = Guid.NewGuid();
        var legacy = $"id,date,type,amount,category,note\r\n{id},2026-10-18,expense,25.50,Food,Lunch\r\n";
        var expense = Assert.Single(LedgerCsv.Parse(legacy));
        Assert.Null(expense.RelatedExpenseId);
        var income = LedgerEntry.Create(Guid.NewGuid(), new(2026, 11, 1), EntryKind.Income, 10m, "AA", "", id);
        var restored = Assert.Single(LedgerCsv.Parse(LedgerCsv.Export([income])));
        Assert.True(income.SameContent(restored));
        Assert.False(income.SameContent(LedgerEntry.Create(income.Id, income.Date, income.Kind, income.Amount, income.Category, income.Note)));
        Assert.DoesNotContain("relation_kind", LedgerCsv.Export([income]));
        Assert.Throws<ArgumentException>(() => LedgerCsv.Parse(LedgerCsv.Export([income]).Replace(id.ToString(), "invalid-guid")));
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
