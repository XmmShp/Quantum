DROP INDEX IF EXISTS "IX_OfficialCalendarEntries___DeletedAtUnixTime";

ALTER TABLE "OfficialCalendarEntries"
    DROP COLUMN "__DeletedAtUnixTime";
