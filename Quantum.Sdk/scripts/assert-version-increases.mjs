import { readFileSync } from "node:fs";
import { compareVersions, parseVersion } from "./compute-version.mjs";

const candidate = parseVersion(process.argv[2]);
if (!candidate) throw new Error(`Invalid candidate version: ${process.argv[2]}`);

const published = process.argv.slice(3)
  .flatMap(path => {
    const value = JSON.parse(readFileSync(path, "utf8"));
    return Array.isArray(value) ? value : (value.versions ?? []);
  })
  .map(parseVersion)
  .filter(Boolean)
  .sort(compareVersions);

const latest = published.at(-1);
if (latest && compareVersions(candidate, latest) < 0) {
  throw new Error(`Version ${candidate.value} is lower than already-published ${latest.value}`);
}

console.log(latest
  ? `Version ${candidate.value} is not lower than published ${latest.value}`
  : `Version ${candidate.value} is the first published version`);
