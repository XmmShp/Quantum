import assert from "node:assert/strict";
import test from "node:test";
import { definePlugin } from "../dist/index.js";

test("definePlugin preserves the lifecycle definition", () => {
  const definition = {
    activate() {},
    mount() {}
  };

  assert.equal(definePlugin(definition), definition);
});
