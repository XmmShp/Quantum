ALTER TABLE "OfficialLedgerEntries" ADD COLUMN "RelatedExpenseId" TEXT NULL;
CREATE INDEX "IX_OfficialLedgerEntries_RelatedExpenseId" ON "OfficialLedgerEntries" ("RelatedExpenseId");
