import assert from "node:assert/strict";
import test from "node:test";
import {
  definePlugin,
  PluginId,
  QuantumTopic,
  SemanticVersion
} from "../dist/index.js";

test("definePlugin preserves the lifecycle definition", () => {
  const definition = {
    activate() {},
    mount() {}
  };

  assert.equal(definePlugin(definition), definition);
});

test("QuantumTopic.of validates the dot-delimited value object", () => {
  assert.equal(QuantumTopic.of("devices.camera.status"), "devices.camera.status");
  assert.equal(QuantumTopic.of("device-1.camera_status.changed-v2"),
    "device-1.camera_status.changed-v2");
  assert.throws(() => QuantumTopic.of("devices/status"), /must match/);
  assert.throws(() => QuantumTopic.of("devices..status"), /must match/);
  assert.throws(() => QuantumTopic.of(" devices.status"), /must match/);
  assert.throws(() => QuantumTopic.of("1devices.status"), /must match/);
  assert.throws(() => QuantumTopic.of("devices._status"), /must match/);
  assert.throws(() => QuantumTopic.of("设备.status"), /must match/);
  assert.throws(() => QuantumTopic.of("devices.status\n"), /must match/);
  assert.throws(() => QuantumTopic.of("a".repeat(256)), /255/);
});

test("PluginId.of normalizes and validates plugin identifiers", () => {
  assert.equal(PluginId.of(" Quantum.Plugin.Example "), "quantum.plugin.example");
  assert.equal(PluginId.of(`a${"b".repeat(127)}`).length, 128);
  assert.equal(PluginId.tryParse("plugin/invalid"), null);
  assert.equal(PluginId.tryParse("disabled"), null);
  assert.equal(PluginId.tryParse("K"), null);
  assert.throws(() => PluginId.of("plugin/invalid"), /plugin id/i);
  assert.throws(() => PluginId.of(`a${"b".repeat(128)}`), /128/);
});

test("SemanticVersion exposes SemVer 2.0 components", () => {
  const version = SemanticVersion.parse("12.34.56-rc.1+linux.arm64");
  const components = SemanticVersion.components(version);

  assert.equal(components.major, 12n);
  assert.equal(components.minor, 34n);
  assert.equal(components.patch, 56n);
  assert.deepEqual(components.preReleaseIdentifiers, ["rc", "1"]);
  assert.deepEqual(components.buildMetadataIdentifiers, ["linux", "arm64"]);
  assert.equal(components.isPreRelease, true);
  assert.equal(components.preRelease, "rc.1");
  assert.equal(components.buildMetadata, "linux.arm64");
});

test("SemanticVersion.compare implements SemVer 2.0 precedence", () => {
  const versions = [
    "1.0.0-alpha",
    "1.0.0-alpha.1",
    "1.0.0-alpha.beta",
    "1.0.0-beta",
    "1.0.0-beta.2",
    "1.0.0-beta.11",
    "1.0.0-rc.1",
    "1.0.0"
  ].map(SemanticVersion.parse);

  for (let index = 1; index < versions.length; index++) {
    assert.equal(SemanticVersion.compare(versions[index - 1], versions[index]), -1);
  }
  assert.equal(
    SemanticVersion.compare(
      SemanticVersion.parse("1.0.0+build.1"),
      SemanticVersion.parse("1.0.0+build.2")
    ),
    0
  );
  assert.equal(
    SemanticVersion.compare(
      SemanticVersion.parse("999999999999999999999999999999.0.0"),
      SemanticVersion.parse("2.0.0")
    ),
    1
  );
});

test("SemanticVersion rejects non-SemVer 2.0 values", () => {
  for (const value of [
    "1",
    "1.2",
    "01.2.3",
    "1.2.3-01",
    "1.2.3-alpha..1",
    "1.2.3+",
    " 1.2.3",
    "1.2.3 "
  ]) {
    assert.equal(SemanticVersion.tryParse(value), null);
    assert.throws(() => SemanticVersion.parse(value), /Semantic Versioning 2\.0\.0/);
  }
});
