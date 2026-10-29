# Quantum Ledger / 记账

Offline CNY ledger for Quantum >= 0.1.7. Records remain in the host-managed plugin SQLite database; no bank connection, telemetry or external account is used.

- Create, edit and delete income/expense entries with dates, custom categories and notes. Money is stored as integer fen with exact decimal totals; positive amounts up to CNY 999,999,999.99 and two decimal places are accepted. Dates: 1900–9998.
- Link any number of entries as neutral, symmetric clues, regardless of income/expense type or month. Each linked entry can discover the other side. Relations never change gross income, expense, balance or category statistics. Changing an entry's type is allowed. Deleting an entry removes only its links; other entries remain intact.
- In the editor, choose an entry month, add one or more entries, and remove selected links with the × control. Changing months preserves previously selected links. Saving replaces the entry's entire link set, including incoming links; an empty selection unlinks it completely.
- Month navigation, search and type filters. Summary and category breakdown cover the whole selected month; search and type filters affect only the transaction list.
- White workspace with muted violet accents, outline action icons and native modal dialogs. Delete once to reveal confirmation, then click the same control again; Escape, blur or a background click cancels confirmation. English/Chinese localization and responsive layout.

## Upgrade to 0.3.0

CSV import/export have been removed from the UI, RPC catalog and package. The `import-csv` and `export-csv` methods are no longer available.

`relatedEntryIds` (an array of GUIDs) replaces the old `relatedExpenseId` field in create/update requests and entry results. This is a public API change. Migration `003_entry_clues.sql` preserves old links in a new symmetric link table without changing record IDs, dates, amounts, categories or notes. The legacy nullable column is retained for upgrade provenance and is no longer used by new code. Database foreign keys remove links when an endpoint is deleted.

## Quantum RPC

The page uses the generated `ILedgerRpcServiceClient`; handlers implement the use cases with repositories and `IDbContext`. Every local-client or host JSON RPC call owns a fresh request scope. Prefix every method below with `quantum.plugin.ledger.ledger.`.

| Method | Request | Result value |
| --- | --- | --- |
| `list` | `{ "month": "2026-10-01" }` | Month entries, each with `relatedEntryIds` |
| `get` | `{ "id": "<guid>" }` | `{ "entry": <entry or null> }` |
| `related` | `{ "id": "<guid>" }` | Directly linked entries across months and types |
| `summary` | `{ "month": "2026-10-01" }` | Gross income, expense, balance and expense category totals |
| `create` | `{ "date": "2026-10-18", "kind": "Expense", "amount": 25.50, "category": "Food", "note": "Lunch", "relatedEntryIds": ["<guid>"] }` | Saved entry including its new ID and relation IDs |
| `update` | `{ "id": "<guid>", "entry": <complete create request> }` | Saved replacement; omitted/null/empty `relatedEntryIds` unlinks all relations |
| `delete` | `{ "id": "<guid>" }` | Non-generic success; absent IDs are idempotent |

Amounts are CNY yuan, dates use `yyyy-MM-dd`, and kind is a string enum (`"Expense"` / `"Income"`) on Quantum 0.1.8; earlier hosts used numeric values (`0` / `1`). Relations are one-hop clues rather than automatic transitive groups. Duplicate IDs are deduplicated. Self-links and empty GUIDs return `ledger_invalid_request`; missing endpoints return `ledger_entry_not_found`. Invalid requests leave both entries and links unchanged. Writes commit atomically; cancellation is propagated. Deletion is permanent and callers should confirm user intent.

Build with `dotnet build Ledger/Quantum.LedgerPlugin.csproj -c Release`. Package `plugin.json`, `Quantum.LedgerPlugin.dll`, localization satellites, `migrations/`, `wwwroot/` and the README from the output directory. The host supplies SDK/NOF assemblies. The full-window workspace uses Quantum's Ledger layout rule.

See [the official plugin RPC guide](../docs/plugin-development.md), [host JSON tests](../tests/Quantum.LedgerPlugin.Tests/LedgerRpcTests.cs) and [migration/local-client tests](../tests/Quantum.LedgerPlugin.Tests/LedgerPersistenceTests.cs).

## Optional calendar integration (0.4.0)

No dependency or integration manifest entry is required. The page checks `calendar.list` and `calendar.get` independently in `quantum.rpc.catalog`, then invokes canonical capability names through `IRpcInvoker`; they need not share a provider. Create/edit an entry to select events or tasks by month; the calendar button displays linked items and identifies deleted items. With no calendar provider the ledger remains usable and existing IDs are preserved.

Create/update requests accept `calendarEntryIds: ["<guid>"]`, and every entry response includes this array. IDs are deduplicated and may span months and event/task kinds. At most 100 non-empty GUIDs are accepted. Updates omitting or passing null preserve existing IDs; an empty array removes them. Links and entry changes commit atomically. References may be saved while calendar is offline; they do not imply foreign keys to another plugin's tables. Calendar deletion leaves a reference which can be removed in the ledger editor. Deleting a ledger entry deletes its references.

`ledger.by-calendar-entry` takes `{ "calendarEntryId": "<guid>" }` and returns all matching entry details across months, sorted by date descending and ID. Missing targets or no links return `[]`; an empty GUID returns `ledger_invalid_request`. Cancellation propagates. Migration `004_calendar_links.sql` adds a default empty JSON array to existing entries without changing their content or amounts.

A replacement ledger must provide the `ledger.by-calendar-entry` capability with this request/result contract for calendar-side viewing. Returned fields used by calendar are `id`, `date`, `kind` (`"Expense"` / `"Income"` on Quantum 0.1.8; 0 / 1 on earlier hosts), `amount` (CNY yuan), `category`, and `note`. The provider owns persistent linkage and offers its own editing UI/API. No official plugin ID, CLR type, assembly reference, or route is required.

## Mobile preview

This build publishes a mobile page on compatible Quantum hosts. See [mobile setup, supported workflows, refresh behavior and new RPC methods](../docs/mobile.md). The desktop remains responsible for business execution; these mobile adaptations are not yet a marketplace release.
