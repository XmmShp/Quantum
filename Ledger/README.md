# Quantum Ledger / 记账

An offline CNY personal ledger for Quantum >= 0.1.0. Records remain in the host-managed plugin SQLite database. No bank connection, network request, telemetry or external account is used.

- Create, edit and delete income/expense entries with dates, custom categories and notes.
- Month navigation, text search and type filters. Summary and category breakdown always cover the entire selected month; search only filters the transaction list.
- Money is stored as integer fen, with exact decimal totals. Positive amounts up to CNY 999,999,999.99 and two decimal places are accepted. Dates: 1900–9998.
- English and Chinese UI, responsive layout, explicit delete confirmation.
- Export all records in the selected month as UTF-8 CSV; import previews totals before confirmation. Up to 1,000 rows / 2 MiB per import. Split larger exports into smaller files before importing.

## CSV format

```csv
id,date,type,amount,category,note
11111111-1111-1111-1111-111111111111,2026-10-18,expense,25.50,Food,Lunch
22222222-2222-2222-2222-222222222222,2026-10-18,income,100.00,Salary,Example
```

Dates and numbers use invariant formatting. `type` is `income` or `expense`; IDs must be non-empty unique GUIDs. Reimporting an unchanged export skips existing IDs. Any ID conflict rejects the entire file; imports commit atomically. Original dates are retained, including records outside the month on screen.

Export quotes all fields and protects spreadsheet formula prefixes with an apostrophe. The importer reverses this protection for lossless round trips, including literal apostrophes. Notes support quotes, commas and newlines. No spreadsheet formulas are evaluated.

Build: `dotnet build Ledger/Quantum.LedgerPlugin.csproj -c Release`. Package `plugin.json`, `Quantum.LedgerPlugin.dll`, localization satellites, `migrations/`, `wwwroot/` and the README from the output folder. The host supplies the shared SDK and NOF assemblies. Install the ZIP in Quantum or publish through Quantum Platform for automated review.

## Quantum RPC (since 0.1.1)

The plugin exposes the `ILedgerRpcService` NOF contract with `[TransportOverQuantum]`. Quantum and AI tools discover it through `quantum.rpc.catalog`; the page and RPC handlers share `LedgerService` and the same local database. Version 0.1.0 had only the page/application service and exposed no RPC capabilities.

Prefix every method below with `quantum.plugin.ledger.ledger.` for a fully qualified name. Each request is a JSON object. All monetary values use CNY yuan with exact decimal arithmetic; `kind` is numeric (`0` expense, `1` income). Dates use `yyyy-MM-dd`.

| Method | Request | Result value |
| --- | --- | --- |
| `list` | `{ "month": "2026-10-01" }` | All entries in that month |
| `get` | `{ "id": "<guid>" }` | `{ "entry": <entry or null> }` |
| `summary` | `{ "month": "2026-10-01" }` | Month, CNY currency, count, income, expense, balance and expense category totals |
| `create` | `{ "date": "2026-10-18", "kind": 0, "amount": 25.50, "category": "Food", "note": "Lunch" }` | Saved entry including its new ID |
| `update` | `{ "id": "<guid>", "entry": <complete create request> }` | Saved replacement entry with the same ID |
| `delete` | `{ "id": "<guid>" }` | Non-generic success Result; deleting an absent ID is idempotent |
| `export-csv` | `{ "month": "2026-10-01" }` | `fileName`, `contentType`, `csv`; no filesystem write |
| `import-csv` | `{ "csv": "id,date,type,amount,category,note\r\n..." }` | `{ "addedCount": 0 }` for an identical reimport |

Responses use NOF `Result` / `Result<T>`. Invalid input returns `ledger_invalid_request`; updating a missing ID returns `ledger_entry_not_found`. Cancellation is propagated. Import checks both the UTF-8 2 MiB limit and the existing 1,000-row/unique-ID/atomic-conflict rules. Deletion is permanent; callers should confirm user intent before destructive calls.

See [the official plugin NOF RPC guide](../docs/plugin-development.md) and [host RPC integration tests](../tests/Quantum.LedgerPlugin.Tests/LedgerRpcTests.cs). Keep the NOF source generators enabled: the host discovers generated `ServiceType` and `HandlerMappings`, then resolves each registered generated handler base type.
