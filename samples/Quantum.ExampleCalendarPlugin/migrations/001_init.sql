CREATE TABLE "CalendarPluginItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CalendarPluginItems" PRIMARY KEY,
    "Title" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "Date" TEXT NOT NULL,
    "StartTime" TEXT NOT NULL,
    "Style" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "TenantId" TEXT NOT NULL,
    "__DeletedAtUnixTime" INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX "IX_CalendarPluginItems___DeletedAtUnixTime"
    ON "CalendarPluginItems" ("__DeletedAtUnixTime");

CREATE INDEX "IX_CalendarPluginItems_Date_StartTime"
    ON "CalendarPluginItems" ("Date", "StartTime");

CREATE INDEX "IX_CalendarPluginItems_TenantId"
    ON "CalendarPluginItems" ("TenantId");
