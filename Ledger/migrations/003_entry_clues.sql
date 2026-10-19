CREATE TABLE "OfficialLedgerEntryLinks" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OfficialLedgerEntryLinks" PRIMARY KEY,
    "LeftEntryId" TEXT NOT NULL,
    "RightEntryId" TEXT NOT NULL,
    "TenantId" TEXT NOT NULL,
    CONSTRAINT "CK_OfficialLedgerEntryLinks_Distinct" CHECK ("LeftEntryId" < "RightEntryId"),
    FOREIGN KEY ("LeftEntryId") REFERENCES "OfficialLedgerEntries" ("Id") ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED,
    FOREIGN KEY ("RightEntryId") REFERENCES "OfficialLedgerEntries" ("Id") ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED
);
CREATE INDEX "IX_OfficialLedgerEntryLinks_LeftEntryId" ON "OfficialLedgerEntryLinks" ("LeftEntryId");
CREATE INDEX "IX_OfficialLedgerEntryLinks_RightEntryId" ON "OfficialLedgerEntryLinks" ("RightEntryId");
CREATE INDEX "IX_OfficialLedgerEntryLinks_TenantId" ON "OfficialLedgerEntryLinks" ("TenantId");
CREATE UNIQUE INDEX "IX_OfficialLedgerEntryLinks_Pair" ON "OfficialLedgerEntryLinks" ("TenantId", "LeftEntryId", "RightEntryId");

-- Preserve legacy links as neutral, symmetric clues. The original nullable column
-- is retained for upgrade provenance; new code no longer reads or writes it.
INSERT INTO "OfficialLedgerEntryLinks" ("Id", "LeftEntryId", "RightEntryId", "TenantId")
SELECT e."Id",
       CASE WHEN e."Id" < e."RelatedExpenseId" THEN e."Id" ELSE e."RelatedExpenseId" END,
       CASE WHEN e."Id" < e."RelatedExpenseId" THEN e."RelatedExpenseId" ELSE e."Id" END,
       e."TenantId"
FROM "OfficialLedgerEntries" e
JOIN "OfficialLedgerEntries" target ON target."Id" = e."RelatedExpenseId" AND target."TenantId" = e."TenantId"
WHERE e."RelatedExpenseId" IS NOT NULL AND e."RelatedExpenseId" <> e."Id";
