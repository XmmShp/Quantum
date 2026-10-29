import { execFileSync, spawnSync } from "node:child_process";
import { appendFileSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const semverPattern = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?$/;

export function parseVersion(value) {
  const match = semverPattern.exec(value);
  if (!match) return undefined;
  return {
    value,
    major: BigInt(match[1]),
    minor: BigInt(match[2]),
    patch: BigInt(match[3]),
    prerelease: match[4],
  };
}

export function compareCore(left, right) {
  for (const key of ["major", "minor", "patch"]) {
    if (left[key] < right[key]) return -1;
    if (left[key] > right[key]) return 1;
  }
  return 0;
}

export function compareVersions(left, right) {
  const core = compareCore(left, right);
  if (core !== 0) return core;
  if (!left.prerelease && !right.prerelease) return 0;
  if (!left.prerelease) return 1;
  if (!right.prerelease) return -1;

  const leftParts = left.prerelease.split(".");
  const rightParts = right.prerelease.split(".");
  for (let index = 0; index < Math.max(leftParts.length, rightParts.length); index += 1) {
    if (leftParts[index] === undefined) return -1;
    if (rightParts[index] === undefined) return 1;
    if (leftParts[index] === rightParts[index]) continue;
    const leftNumeric = /^\d+$/.test(leftParts[index]);
    const rightNumeric = /^\d+$/.test(rightParts[index]);
    if (leftNumeric && rightNumeric) {
      return BigInt(leftParts[index]) < BigInt(rightParts[index]) ? -1 : 1;
    }
    if (leftNumeric !== rightNumeric) return leftNumeric ? -1 : 1;
    return leftParts[index] < rightParts[index] ? -1 : 1;
  }
  return 0;
}

export function computeNightlyVersion(tags, initialVersion, date, runNumber) {
  const tagged = tags
    .map(tag => tag.replace(/^v/, ""))
    .map(parseVersion)
    .filter(Boolean)
    .sort(compareVersions);

  const initial = parseVersion(initialVersion.replace(/^v/, ""));
  if (!initial) throw new Error(`Invalid initial version: ${initialVersion}`);

  const base = tagged.length === 0
    ? initial
    : { ...tagged.at(-1), patch: tagged.at(-1).patch + 1n };

  return `${base.major}.${base.minor}.${base.patch}-nightly.${date}.${runNumber}`;
}

function output(name, value) {
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, `${name}=${value}\n`);
  } else {
    console.log(`${name}=${value}`);
  }
}

function main() {
  const recoveryVersion = process.env.RECOVERY_VERSION?.trim();
  if (recoveryVersion) {
    const version = parseVersion(recoveryVersion);
    if (!version) throw new Error(`Invalid recovery version: ${recoveryVersion}`);
    output("publish", "true");
    output("version", version.value);
    output("channel", "recovery");
    output("npm_tag", version.prerelease?.startsWith("nightly.") ? "nightly" : (version.prerelease ? "next" : "latest"));
    return;
  }

  const refType = process.env.GITHUB_REF_TYPE ?? "branch";
  const refName = process.env.GITHUB_REF_NAME ?? "main";

  if (refType === "tag") {
    const ancestry = spawnSync("git", ["merge-base", "--is-ancestor", "HEAD", "refs/remotes/origin/main"]);
    if (ancestry.status !== 0) {
      output("publish", "false");
      output("reason", "tag commit is not reachable from main");
      return;
    }

    const versionText = refName.replace(/^v/, "");
    const version = parseVersion(versionText);
    if (!version) {
      output("publish", "false");
      output("reason", "tag is not supported SemVer (build metadata is intentionally excluded)");
      return;
    }

    output("publish", "true");
    output("version", version.value);
    output("channel", version.prerelease ? "prerelease" : "stable");
    output("npm_tag", version.prerelease ? "next" : "latest");
    return;
  }

  if (refName !== "main") {
    output("publish", "false");
    output("reason", "branch is not main");
    return;
  }

  const packageJson = JSON.parse(readFileSync("typescript/package.json", "utf8"));
  const tags = execFileSync("git", ["tag", "--merged", "HEAD", "--list"], { encoding: "utf8" })
    .split(/\r?\n/)
    .filter(Boolean);
  const date = new Date().toISOString().slice(0, 10).replaceAll("-", "");
  const version = computeNightlyVersion(
    tags,
    packageJson.version,
    date,
    process.env.GITHUB_RUN_NUMBER ?? "0",
  );

  output("publish", "true");
  output("version", version);
  output("channel", "nightly");
  output("npm_tag", "nightly");
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  main();
}
