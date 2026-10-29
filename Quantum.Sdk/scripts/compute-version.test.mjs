import assert from "node:assert/strict";
import test from "node:test";
import { compareVersions, computeNightlyVersion, parseVersion } from "./compute-version.mjs";

test("uses the package core before the first stable tag", () => {
  assert.equal(
    computeNightlyVersion([], "0.1.0-alpha.1", "20260914", "42"),
    "0.1.0-nightly.20260914.42",
  );
});

test("increments patch after the greatest SemVer tag", () => {
  assert.equal(
    computeNightlyVersion(["v1.3.9", "1.4.0", "v2.0.0-rc.1"], "0.1.0", "20260914", "43"),
    "2.0.1-nightly.20260914.43",
  );
});

test("accepts strict SemVer prereleases but rejects build metadata", () => {
  assert.equal(parseVersion("1.2.3-rc.1")?.prerelease, "rc.1");
  assert.equal(parseVersion("1.2.3+build.1"), undefined);
  assert.equal(parseVersion("01.2.3"), undefined);
});

test("compares SemVer prerelease precedence", () => {
  assert.equal(compareVersions(parseVersion("1.0.0-rc.1"), parseVersion("1.0.0")), -1);
  assert.equal(compareVersions(parseVersion("1.0.0-beta.11"), parseVersion("1.0.0-rc.1")), -1);
  assert.equal(compareVersions(parseVersion("2.0.0"), parseVersion("1.99.99")), 1);
});
