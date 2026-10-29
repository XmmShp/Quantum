using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

internal sealed class NativeAudioRecorder : IAsyncDisposable
{
    private string? _file;
#if WINDOWS
    private Windows.Media.Capture.MediaCapture? _capture;
#elif ANDROID
    private Android.Media.AudioRecord? _audio;
    private CancellationTokenSource? _audioCancellation;
    private Task? _audioTask;
#elif IOS || MACCATALYST
    private AVFoundation.AVAudioRecorder? _appleRecorder;
#endif
    public async Task StartAsync()
    {
#if WINDOWS
        _capture = new Windows.Media.Capture.MediaCapture();
        await _capture.InitializeAsync(new Windows.Media.Capture.MediaCaptureInitializationSettings { StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Audio });
        var file = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(FileSystem.CacheDirectory);
        var recording = await file.CreateFileAsync("quantum-voice-" + Guid.NewGuid().ToString("N") + ".wav");
        _file = recording.Path;
        var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(Windows.Media.MediaProperties.AudioEncodingQuality.Low);
        profile.Audio = Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm(16000, 1, 16);
        await _capture.StartRecordToStorageFileAsync(profile, recording);
#elif ANDROID
        if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            throw new BridgeException("mobile_microphone_denied", "Microphone permission is required.");
        _file = Path.Combine(FileSystem.CacheDirectory, "quantum-voice-" + Guid.NewGuid().ToString("N") + ".wav");
        var minimum = Android.Media.AudioRecord.GetMinBufferSize(16000, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit);
        if (minimum <= 0) throw new InvalidOperationException("Audio recording is unavailable.");
        var audio = _audio = new Android.Media.AudioRecord(Android.Media.AudioSource.Mic, 16000, Android.Media.ChannelIn.Mono,
            Android.Media.Encoding.Pcm16bit, Math.Max(8192, minimum));
        if (audio.State != Android.Media.State.Initialized) throw new InvalidOperationException("Audio recording is unavailable.");
        _audioCancellation = new();
        var token = _audioCancellation.Token;
        var path = _file;
        audio.StartRecording();
        _audioTask = Task.Run(async () =>
        {
            await using var stream = File.Create(path);
            await stream.WriteAsync(PcmWaveHeader.Create(0));
            var buffer = new byte[Math.Max(8192, minimum)];
            var count = 0;
            while (!token.IsCancellationRequested)
            {
                var read = audio.Read(buffer, 0, buffer.Length);
                if (read < 0 && !token.IsCancellationRequested) throw new IOException("Audio capture failed.");
                if (read > 0) { await stream.WriteAsync(buffer.AsMemory(0, read)); count += read; }
            }
            stream.Position = 0;
            await stream.WriteAsync(PcmWaveHeader.Create(count));
            await stream.FlushAsync();
        });
#elif IOS || MACCATALYST
        if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            throw new BridgeException("mobile_microphone_denied", "Microphone permission is required.");
        var session = AVFoundation.AVAudioSession.SharedInstance();
        if (!session.SetCategory(AVFoundation.AVAudioSessionCategory.Record, (AVFoundation.AVAudioSessionCategoryOptions)0, out _) || !session.SetActive(true, out _))
            throw new InvalidOperationException("Audio session is unavailable.");
        _file = Path.Combine(FileSystem.CacheDirectory, "quantum-voice-" + Guid.NewGuid().ToString("N") + ".wav");
        _appleRecorder = AVFoundation.AVAudioRecorder.Create(Foundation.NSUrl.FromFilename(_file),
            new AVFoundation.AudioSettings { Format = AudioToolbox.AudioFormatType.LinearPCM, SampleRate = 16000, NumberChannels = 1, LinearPcmBitDepth = 16, LinearPcmFloat = false, LinearPcmBigEndian = false, AudioQuality = AVFoundation.AVAudioQuality.Medium }, out _);
        if (_appleRecorder is null || !_appleRecorder.PrepareToRecord() || !_appleRecorder.Record())
            throw new InvalidOperationException("Audio recording could not start.");
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("Audio recording is not available on this target yet.");
#endif
    }

    public async Task<BridgeAttachment> StopAsync()
    {
#if WINDOWS
        if (_capture is not null) await _capture.StopRecordAsync();
#elif ANDROID
        await StopAndroidAsync();
#elif IOS || MACCATALYST
        _appleRecorder?.Stop();
#endif
        if (_file is null) throw new InvalidOperationException("Recording has not started.");
        await using var stream = File.OpenRead(_file);
        return await OutgoingAttachmentStore.SaveAsync(stream, "voice-" + DateTime.Now.ToString("HHmmss") + Path.GetExtension(_file),
            _file.EndsWith(".wav", StringComparison.Ordinal) ? "audio/wav" : "audio/mp4");
    }

#if ANDROID
    private async Task StopAndroidAsync()
    {
        _audioCancellation?.Cancel();
        try { _audio?.Stop(); } catch (Java.Lang.IllegalStateException) { }
        if (_audioTask is { } task) { _audioTask = null; await task; }
    }
#endif

    public async ValueTask DisposeAsync()
    {
#if WINDOWS
        _capture?.Dispose(); _capture = null;
#elif ANDROID
        try { await StopAndroidAsync(); }
        finally
        {
            _audio?.Release(); _audio?.Dispose(); _audio = null;
            _audioCancellation?.Dispose(); _audioCancellation = null;
        }
#elif IOS || MACCATALYST
        _appleRecorder?.Stop(); _appleRecorder?.Dispose(); _appleRecorder = null;
        AVFoundation.AVAudioSession.SharedInstance().SetActive(false, out _);
#endif
        if (_file is not null && File.Exists(_file)) File.Delete(_file);
        _file = null;
        await Task.CompletedTask;
    }
}
