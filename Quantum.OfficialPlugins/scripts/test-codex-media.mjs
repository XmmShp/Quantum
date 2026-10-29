import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const source = readFileSync(new URL('../Codex/wwwroot/codex-media.js', import.meta.url), 'utf8');
const { record, encodeWave } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('recorded samples become bounded mono PCM16 WAV', () => {
    const bytes = encodeWave(new Float32Array([-2, -1, 0, .5, 1, 2]));
    const view = new DataView(bytes.buffer);
    assert.equal(Buffer.from(bytes.subarray(0, 4)).toString(), 'RIFF');
    assert.equal(Buffer.from(bytes.subarray(8, 12)).toString(), 'WAVE');
    assert.equal(view.getUint32(4, true), bytes.length - 8);
    assert.equal(view.getUint16(20, true), 1);
    assert.equal(view.getUint16(22, true), 1);
    assert.equal(view.getUint32(24, true), 16000);
    assert.equal(view.getUint16(34, true), 16);
    assert.deepEqual(Array.from({ length: 6 }, (_, i) => view.getInt16(44 + i * 2, true)), [-32768, -32768, 0, 16383, 32767, 32767]);
    assert.throws(() => encodeWave(new Float32Array(16000 * 47)));
});

test('cancelling a recording releases input tracks and never delivers an attachment', async () => {
    const previousNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
    const previousRecorder = globalThis.MediaRecorder;
    let stopped = 0;
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { mediaDevices: { getUserMedia: async () => ({ getTracks: () => [{ stop() { stopped++; } }] }) } } });
    globalThis.MediaRecorder = class {
        static isTypeSupported() { return true; }
        state = 'inactive';
        start() { this.state = 'recording'; }
        stop() { this.state = 'inactive'; this.onstop?.(); }
    };
    try {
        let deliveries = 0;
        const controller = await record({ invokeMethodAsync: async () => { deliveries++; } });
        controller.cancel();
        assert.ok(stopped > 0);
        assert.equal(deliveries, 0);
    } finally {
        Object.defineProperty(globalThis, 'navigator', previousNavigator);
        globalThis.MediaRecorder = previousRecorder;
    }
});
