export async function record(receiver) {
    if (!navigator.mediaDevices?.getUserMedia || !globalThis.MediaRecorder) throw new Error('Recording unavailable');
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    let recorder;
    try {
        const preferred = ['audio/webm;codecs=opus', 'audio/mp4', 'audio/webm'].find(type => MediaRecorder.isTypeSupported(type));
        recorder = new MediaRecorder(stream, preferred ? { mimeType: preferred } : undefined);
    } catch (error) {
        stream.getTracks().forEach(track => track.stop());
        throw error;
    }
    let cancelled = false;
    let bytes = 0;
    const chunks = [];
    const timer = setTimeout(() => { if (recorder.state !== 'inactive') recorder.stop(); }, 45000);
    const release = () => { clearTimeout(timer); stream.getTracks().forEach(track => track.stop()); };
    recorder.ondataavailable = event => {
        bytes += event.data.size;
        if (bytes > 8 * 1024 * 1024) { cancelled = true; receiver.invokeMethodAsync('RecordingFailedAsync'); if (recorder.state !== 'inactive') recorder.stop(); }
        else if (event.data.size) chunks.push(event.data);
    };
    recorder.onerror = async () => { cancelled = true; release(); await receiver.invokeMethodAsync('RecordingFailedAsync'); };
    recorder.onstop = async () => {
        release();
        if (cancelled || !chunks.length) return;
        try {
            const wav = await recordingToWave(new Blob(chunks, { type: recorder.mimeType }));
            const data = await new Promise((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result).split(',')[1]); reader.onerror = reject; reader.readAsDataURL(wav); });
            if (cancelled) return;
            await receiver.invokeMethodAsync('RecordedAsync', 'voice-' + Date.now() + '.wav', 'audio/wav', data);
        } catch { if (!cancelled) await receiver.invokeMethodAsync('RecordingFailedAsync'); }
    };
    recorder.start(250);
    return { stop() { if (recorder.state !== 'inactive') recorder.stop(); }, cancel() { cancelled = true; if (recorder.state !== 'inactive') recorder.stop(); release(); } };
}

export function encodeWave(samples, sampleRate = 16000) {
    if (!(samples instanceof Float32Array) || samples.length > sampleRate * 46) throw new Error('Recording limit exceeded');
    const bytes = new Uint8Array(44 + samples.length * 2);
    const view = new DataView(bytes.buffer);
    const text = (offset, value) => { for (let i = 0; i < value.length; i++) bytes[offset + i] = value.charCodeAt(i); };
    text(0, 'RIFF'); view.setUint32(4, bytes.length - 8, true); text(8, 'WAVE'); text(12, 'fmt ');
    view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
    view.setUint32(24, sampleRate, true); view.setUint32(28, sampleRate * 2, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
    text(36, 'data'); view.setUint32(40, samples.length * 2, true);
    for (let i = 0; i < samples.length; i++) { const value = Math.max(-1, Math.min(1, samples[i])); view.setInt16(44 + i * 2, value < 0 ? value * 32768 : value * 32767, true); }
    return bytes;
}

async function recordingToWave(blob) {
    const context = new OfflineAudioContext(1, 1, 16000);
    const decoded = await context.decodeAudioData(await blob.arrayBuffer());
    if (decoded.duration > 46) throw new Error('Recording limit exceeded');
    const mono = new Float32Array(decoded.length);
    for (let channel = 0; channel < decoded.numberOfChannels; channel++) {
        const samples = decoded.getChannelData(channel);
        for (let i = 0; i < mono.length; i++) mono[i] += samples[i] / decoded.numberOfChannels;
    }
    return new Blob([encodeWave(mono, decoded.sampleRate)], { type: 'audio/wav' });
}
