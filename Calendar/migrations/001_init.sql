CREATE TABLE "OfficialCalendarEntries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OfficialCalendarEntries" PRIMARY KEY,
    "Title" TEXT NOT NULL,
    "Notes" TEXT NOT NULL,
    "Kind" INTEGER NOT NULL,
    "Date" TEXT NOT NULL,
    "StartTime" TEXT NOT NULL,
    "EndTime" TEXT NULL,
    "IsCompleted" INTEGER NOT NULL DEFAULT 0,
    "Style" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "TenantId" TEXT NOT NULL,
    "__DeletedAtUnixTime" INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX "IX_OfficialCalendarEntries___DeletedAtUnixTime"
    ON "OfficialCalendarEntries" ("__DeletedAtUnixTime");

CREATE INDEX "IX_OfficialCalendarEntries_Date_StartTime"
    ON "OfficialCalendarEntries" ("Date", "StartTime");

CREATE INDEX "IX_OfficialCalendarEntries_Kind_IsCompleted_Date"
    ON "OfficialCalendarEntries" ("Kind", "IsCompleted", "Date");

CREATE INDEX "IX_OfficialCalendarEntries_TenantId"
    ON "OfficialCalendarEntries" ("TenantId");
