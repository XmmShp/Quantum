CREATE TABLE "OfficialLedgerEntries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OfficialLedgerEntries" PRIMARY KEY,
    "Date" TEXT NOT NULL,
    "Kind" INTEGER NOT NULL,
    "AmountInFen" INTEGER NOT NULL,
    "Category" TEXT NOT NULL,
    "Note" TEXT NOT NULL,
    "TenantId" TEXT NOT NULL
);
CREATE INDEX "IX_OfficialLedgerEntries_Date" ON "OfficialLedgerEntries" ("Date");
CREATE INDEX "IX_OfficialLedgerEntries_TenantId" ON "OfficialLedgerEntries" ("TenantId");
