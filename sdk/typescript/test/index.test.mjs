import assert from "node:assert/strict";
import test from "node:test";
import { definePlugin, QuantumTopic } from "../dist/index.js";

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
