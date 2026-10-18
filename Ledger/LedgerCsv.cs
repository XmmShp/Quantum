using System.Globalization;
using System.Text;

namespace Quantum.OfficialPlugins.Ledger;

/// <summary>Invariant, RFC 4180 CSV with stable IDs for idempotent reimport.</summary>
public static class LedgerCsv
{
    public const int MaxRecords = 1000;
    private static readonly string[] Header = ["id", "date", "type", "amount", "category", "note"];

    public static string Export(IEnumerable<LedgerEntry> entries)
    {
        var csv = new StringBuilder("\uFEFF" + string.Join(',', Header) + "\r\n");
        foreach (var item in entries)
        {
            string[] cells = [item.Id.ToString(), item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.Kind == EntryKind.Income ? "income" : "expense", item.Amount.ToString("F2", CultureInfo.InvariantCulture),
                Protect(item.Category), Protect(item.Note)];
            csv.AppendJoin(',', cells.Select(cell => "\"" + cell.Replace("\"", "\"\"") + "\""));
            csv.Append("\r\n");
        }
        return csv.ToString();
    }

    public static IReadOnlyList<LedgerEntry> Parse(string csv)
    {
        if (csv.Length > 2 * 1024 * 1024) throw new ArgumentException("CSV exceeds 2 MiB.");
        var rows = ReadRows(csv.TrimStart('\uFEFF'));
        if (rows.Count == 0 || !rows[0].SequenceEqual(Header))
            throw new ArgumentException("Expected CSV header: id,date,type,amount,category,note");
        if (rows.Count is < 2 or > MaxRecords + 1) throw new ArgumentException("CSV must contain 1–1,000 records.");
        var result = new List<LedgerEntry>();
        var ids = new HashSet<Guid>();
        for (var index = 1; index < rows.Count; index++)
        {
            var cells = rows[index];
            if (cells.Length != 6 || !Guid.TryParse(cells[0], out var id) || !ids.Add(id) ||
                !DateOnly.TryParseExact(cells[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                !decimal.TryParse(cells[3], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
                throw new ArgumentException($"Invalid CSV record {index}.");
            var kind = cells[2] switch
            {
                "income" => EntryKind.Income,
                "expense" => EntryKind.Expense,
                _ => throw new ArgumentException($"Invalid type in CSV record {index}.")
            };
            result.Add(LedgerEntry.Create(id, date, kind, amount, Unprotect(cells[4]), Unprotect(cells[5])));
        }
        return result;
    }

    private static bool UnsafeStart(char value) => value is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n' or '\'';
    private static string Protect(string value) => value.Length > 0 && UnsafeStart(value[0]) ? "'" + value : value;
    private static string Unprotect(string value) => value.Length > 1 && value[0] == '\'' && UnsafeStart(value[1]) ? value[1..] : value;

    private static List<string[]> ReadRows(string csv)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false, closed = false, started = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (quoted)
            {
                if (ch != '"') cell.Append(ch);
                else if (i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                else { quoted = false; closed = true; }
                continue;
            }
            if (ch == ',' || ch == '\r' || ch == '\n')
            {
                row.Add(cell.ToString()); cell.Clear(); closed = false; started = false;
                if (ch == ',') continue;
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                rows.Add(row.ToArray()); row.Clear();
                if (rows.Count > MaxRecords + 1) throw new ArgumentException("CSV contains too many records.");
            }
            else if (closed) throw new ArgumentException("Unexpected characters after a CSV quote.");
            else if (ch == '"')
            {
                if (started) throw new ArgumentException("Unexpected quote in CSV field.");
                quoted = true; started = true;
            }
            else { cell.Append(ch); started = true; }
        }
        if (quoted) throw new ArgumentException("Unclosed CSV quote.");
        if (started || closed || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}
