using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using HyperFidel.Localization;


namespace HyperFidel.Audio;

/// <summary>
/// Plays decoded stereo PCM through direct ALSA hardware or OpenAL on Linux.
/// The historical type name is retained so shared routing needs no platform branch.
/// </summary>
public sealed class WasapiAudioPlayer : IGaplessAudioPlayer, IEqualizerAudioPlayer
{
    private const int BufferCount = 8;
    private const int BufferSize = 64 * 1024;
    private readonly IReadOnlyList<GaplessPlaybackItem> _items;
    private readonly AudioFileInfo?[] _infos;
    private readonly long[] _trackStartFrames;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _requestLock = new();
    private readonly nint _device;
    private readonly nint _context;
    private nint _alsaPcm;
    private int _sampleRate;
    private ParametricEqualizer _equalizer;
    private readonly string _alsaDeviceName;
    private readonly int _maxOutputSampleRateHz;
    private bool _equalizerEnabled;
    private EqualizerProfile? _equalizerProfile;
    private readonly PipeWireExclusiveHandoff? _systemMixerHandoff;
    private readonly Task _pumpTask;
    private EqualizerUpdateRequest? _pendingEqualizerUpdate;
    private SeekRequest? _pendingSeek;
    private long _playedFrames;
    private int _audibleTrackIndex;
    private volatile bool _paused;
    private volatile bool _stopAfterCurrentTrack;
    private volatile float _volume = 0.0f;
    private bool _disposed;

    private WasapiAudioPlayer(
        IReadOnlyList<GaplessPlaybackItem> items,
        AudioFileInfo firstInfo,
        nint device,
        nint context,
        nint alsaPcm,
        int sampleRate,
        bool equalizerEnabled,
        EqualizerProfile? equalizerProfile,
        string alsaDeviceName,
        int maxOutputSampleRateHz,
        PipeWireExclusiveHandoff? systemMixerHandoff)
    {
        _items = items;
        _infos = new AudioFileInfo?[items.Count];
        _infos[0] = firstInfo;
        _trackStartFrames = new long[items.Count];
        _device = device;
        _context = context;
        _alsaPcm = alsaPcm;
        _sampleRate = sampleRate;
        _alsaDeviceName = alsaDeviceName;
        _maxOutputSampleRateHz = maxOutputSampleRateHz;
        _equalizerEnabled = equalizerEnabled;
        _equalizerProfile = equalizerProfile?.Clone();
        _systemMixerHandoff = systemMixerHandoff;
        _equalizer = new ParametricEqualizer(sampleRate, _equalizerEnabled, _equalizerProfile);
        _pumpTask = Task.Run(PumpAsync);
    }

    /// <inheritdoc/>
    public event EventHandler<GaplessTrackChangedEventArgs>? TrackChanged;
    /// <inheritdoc/>
    public string CurrentFilePath => _items[Volatile.Read(ref _audibleTrackIndex)].FilePath;
    /// <inheritdoc/>
    public AudioFileInfo CurrentInfo =>
        _infos[Volatile.Read(ref _audibleTrackIndex)]
        ?? throw new InvalidOperationException("Current track information is unavailable.");
    /// <inheritdoc/>
    public TimeSpan Duration => CurrentInfo.Duration;
    /// <inheritdoc/>
    public TimeSpan Position
    {
        get
        {
            var index = Volatile.Read(ref _audibleTrackIndex);
            var relativeFrames = Math.Max(
                0,
                Interlocked.Read(ref _playedFrames) -
                Volatile.Read(ref _trackStartFrames[index]));
            return TimeSpan.FromSeconds(
                Math.Min(relativeFrames / (double)_sampleRate, Duration.TotalSeconds));
        }
    }
    /// <inheritdoc/>
    public bool IsPaused => _paused;
    /// <inheritdoc/>
    public bool CanSeek => Duration > TimeSpan.Zero;
    /// <inheritdoc/>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0f, 1.0f);
    }
    /// <inheritdoc/>
    public float ReplayGainFactor { get; set; } = 1.0f;
    /// <inheritdoc/>
    public bool StopAfterCurrentTrack
    {
        get => _stopAfterCurrentTrack;
        set => _stopAfterCurrentTrack = value;
    }

    /// <summary>Creates a continuous direct ALSA or OpenAL playback session.</summary>
    /// <param name="items">Tracks in playback order with their ReplayGain factors.</param>
    /// <param name="deviceId">Direct ALSA identifier prefixed with <c>alsa:</c>, OpenAL name, or <c>default</c>.</param>
    /// <param name="equalizerEnabled">Whether the supplied equalizer profile is active.</param>
    /// <param name="equalizerProfile">Equalizer profile applied to PCM samples.</param>
    /// <param name="cancellationToken">Cancellation token for startup.</param>
    /// <param name="maxOutputSampleRateHz">
    /// Configured maximum PCM output rate in hertz, or zero for automatic. The shared
    /// Linux and macOS output paths honour it as well, so the option behaves the same on
    /// every platform.
    /// </param>
    /// <returns>The player and technical information for the first track.</returns>
    public static async Task<(WasapiAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        IReadOnlyList<GaplessPlaybackItem> items,
        string deviceId,
        bool equalizerEnabled = false,
        EqualizerProfile? equalizerProfile = null,
        CancellationToken cancellationToken = default,
        int maxOutputSampleRateHz = 0)
    {
        if (items.Count == 0)
            throw new ArgumentException("At least one playback item is required.", nameof(items));

        var info = items[0].TryCreateKnownAudioInfo()
                   ?? await ProbeAsync(items[0].PlaybackPath, cancellationToken).ConfigureAwait(false);
        info = ApplyKnownDuration(items[0], info);
        var sourceRate = NormalizePcmRate(info.OutputSampleRate);
        if (maxOutputSampleRateHz > 0)
            sourceRate = Math.Min(sourceRate, maxOutputSampleRateHz);

        if (deviceId.StartsWith("alsa:", StringComparison.Ordinal))
        {
            // PipeWire/PulseAudio normally keep the USB DAC open even when idle.
            // Release the matching card profile before opening the exclusive
            // hardware PCM, then restore it when this player is retired.
            var handoff = PipeWireExclusiveHandoff.TrySuspend(deviceId);
            AlsaHardwareVolume.EnsureUnity(deviceId);
            nint pcm;
            try
            {
                pcm = AlsaNative.OpenExact(deviceId["alsa:".Length..], sourceRate);
            }
            catch
            {
                handoff?.Dispose();
                throw;
            }
            info = info with { OutputSampleRate = sourceRate };
            var alsaPlayer = new WasapiAudioPlayer(
                items,
                info,
                nint.Zero,
                nint.Zero,
                pcm,
                sourceRate,
                equalizerEnabled,
                equalizerProfile,
                deviceId["alsa:".Length..],
                maxOutputSampleRateHz,
                handoff);
            try
            {
                await alsaPlayer._started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return (alsaPlayer, info);
            }
            catch
            {
                alsaPlayer.Dispose();
                throw;
            }
        }

        var requestedDevice = string.Equals(deviceId, "default", StringComparison.OrdinalIgnoreCase)
            ? null
            : deviceId;
        var device = OpenAlNative.OpenDevice(requestedDevice);
        if (device == nint.Zero)
            throw new InvalidOperationException(LocalizationManager.Current.OpenAlInitializationFailed);
        var context = OpenAlNative.AlcCreateContext(device, sourceRate);
        if (context == nint.Zero)
            context = OpenAlNative.AlcCreateContext(device, 0);
        if (context == nint.Zero)
        {
            OpenAlNative.AlcCloseDevice(device);
            throw new InvalidOperationException(LocalizationManager.Current.OpenAlInitializationFailed);
        }

        if (!OpenAlNative.AlcMakeContextCurrent(context))
        {
            OpenAlNative.AlcDestroyContext(context);
            OpenAlNative.AlcCloseDevice(device);
            throw new InvalidOperationException(LocalizationManager.Current.OpenAlInitializationFailed);
        }
        var mixerRate = OpenAlNative.GetMixerFrequency(device);
        OpenAlNative.AlcMakeContextCurrent(nint.Zero);
        var sampleRate = mixerRate > 0 ? mixerRate : sourceRate;
        info = info with { OutputSampleRate = sampleRate };

        var player = new WasapiAudioPlayer(
            items,
            info,
            device,
            context,
            nint.Zero,
            sampleRate,
            equalizerEnabled,
            equalizerProfile,
            string.Empty,
            maxOutputSampleRateHz,
            null);
        try
        {
            await player._started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return (player, info);
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }

    /// <summary>Creates a single-track direct ALSA or OpenAL playback session.</summary>
    /// <param name="filePath">Local path or supported stream URL.</param>
    /// <param name="deviceId">Direct ALSA identifier, OpenAL device name, or <c>default</c>.</param>
    /// <param name="equalizerEnabled">Whether equalization is active.</param>
    /// <param name="equalizerProfile">Equalizer profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="maxOutputSampleRateHz">
    /// Configured maximum PCM output rate in hertz, or zero for automatic.
    /// </param>
    /// <returns>The player and technical information.</returns>
    public static Task<(WasapiAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        string deviceId,
        bool equalizerEnabled = false,
        EqualizerProfile? equalizerProfile = null,
        CancellationToken cancellationToken = default,
        int maxOutputSampleRateHz = 0) =>
        CreateAsync(
            [new GaplessPlaybackItem(filePath, 1.0f)],
            deviceId,
            equalizerEnabled,
            equalizerProfile,
            cancellationToken,
            maxOutputSampleRateHz);

    /// <inheritdoc/>
    public void UpdateEqualizer(bool enabled, EqualizerProfile? profile) =>
        Interlocked.Exchange(
            ref _pendingEqualizerUpdate,
            new EqualizerUpdateRequest(enabled, profile?.Clone()));

    /// <inheritdoc/>
    public void Pause() => _paused = true;
    /// <inheritdoc/>
    public void Resume() => _paused = false;

    /// <inheritdoc/>
    public Task SeekAsync(TimeSpan position)
    {
        position = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > Duration ? Duration : position;
        var request = new SeekRequest(position);
        lock (_requestLock)
        {
            _pendingSeek?.Completion.TrySetCanceled();
            _pendingSeek = request;
        }
        return request.Completion.Task;
    }

    /// <inheritdoc/>
    public Task WaitForCompletionAsync() => _pumpTask;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        try { _pumpTask.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _systemMixerHandoff?.Dispose();
        _cts.Dispose();
    }

    private async Task PumpAsync()
    {
        if (_alsaPcm != nint.Zero)
        {
            await PumpAlsaAsync().ConfigureAwait(false);
            return;
        }

        await PumpOpenAlAsync().ConfigureAwait(false);
    }

    private async Task PumpOpenAlAsync()
    {
        uint source = 0;
        var buffers = new uint[BufferCount];
        FfmpegPcmDecoder? decoder = null;
        var freeBuffers = new Queue<uint>();
        var queuedFrames = new Dictionary<uint, int>();
        var decodeBuffer = new byte[BufferSize];
        var writeTrackIndex = 0;
        long completedFrames = 0;
        var decoderFinished = false;

        try
        {
            if (!OpenAlNative.AlcMakeContextCurrent(_context))
                throw new InvalidOperationException(LocalizationManager.Current.OpenAlInitializationFailed);
            OpenAlNative.AlGenSources(1, out source);
            OpenAlNative.AlGenBuffers(BufferCount, buffers);
            foreach (var buffer in buffers)
                freeBuffers.Enqueue(buffer);

            decoder = await CreateDecoderAsync(0, TimeSpan.Zero, _cts.Token).ConfigureAwait(false);
            _started.TrySetResult();

            while (!_cts.IsCancellationRequested)
            {
                ApplyControlRequests(source, ref decoder, ref writeTrackIndex, ref completedFrames, freeBuffers, queuedFrames);
                OpenAlNative.AlSourcef(source, OpenAlNative.AlGain, _volume * _volume * _volume);

                OpenAlNative.AlGetSourcei(source, OpenAlNative.AlBuffersProcessed, out var processed);
                while (processed-- > 0)
                {
                    OpenAlNative.AlSourceUnqueueBuffers(source, 1, out var buffer);
                    if (queuedFrames.Remove(buffer, out var frames))
                        completedFrames += frames;
                    freeBuffers.Enqueue(buffer);
                }

                OpenAlNative.AlGetSourcei(source, OpenAlNative.AlSampleOffset, out var sampleOffset);
                Interlocked.Exchange(ref _playedFrames, completedFrames + Math.Max(0, sampleOffset));
                UpdateAudibleTrack();

                while (!decoderFinished && freeBuffers.Count > 0)
                {
                    var bytesRead = await decoder!.ReadAsync(decodeBuffer, _cts.Token).ConfigureAwait(false);
                    bytesRead -= bytesRead % 4;
                    if (bytesRead == 0)
                    {
                        decoder.Dispose();
                        decoder = null;
                        var nextIndex = writeTrackIndex + 1;
                        if (_stopAfterCurrentTrack || nextIndex >= _items.Count)
                        {
                            decoderFinished = true;
                            break;
                        }

                        writeTrackIndex = nextIndex;
                        Volatile.Write(
                            ref _trackStartFrames[nextIndex],
                            completedFrames + queuedFrames.Values.Sum(static value => (long)value));
                        var nextInfo = _items[nextIndex].TryCreateKnownAudioInfo()
                                       ?? await ProbeAsync(_items[nextIndex].PlaybackPath, _cts.Token).ConfigureAwait(false);
                        _infos[nextIndex] = ApplyKnownDuration(_items[nextIndex], nextInfo)
                            with { OutputSampleRate = _sampleRate };
                        decoder = await CreateDecoderAsync(nextIndex, TimeSpan.Zero, _cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    ProcessPcm16(decodeBuffer.AsSpan(0, bytesRead), _items[writeTrackIndex].ReplayGainFactor);
                    var buffer = freeBuffers.Dequeue();
                    OpenAlNative.AlBufferData(
                        buffer,
                        OpenAlNative.AlFormatStereo16,
                        decodeBuffer,
                        bytesRead,
                        _sampleRate);
                    OpenAlNative.AlSourceQueueBuffers(source, 1, ref buffer);
                    queuedFrames[buffer] = bytesRead / 4;
                }

                OpenAlNative.AlGetSourcei(source, OpenAlNative.AlSourceState, out var state);
                OpenAlNative.AlGetSourcei(source, OpenAlNative.AlBuffersQueued, out var queued);
                if (_paused)
                {
                    if (state == OpenAlNative.AlPlaying)
                        OpenAlNative.AlSourcePause(source);
                }
                else if (queued > 0 && state != OpenAlNative.AlPlaying)
                {
                    OpenAlNative.AlSourcePlay(source);
                }

                if (decoderFinished && queued == 0)
                    break;
                await Task.Delay(5, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            _started.TrySetCanceled(_cts.Token);
        }
        catch (Exception ex)
        {
            _started.TrySetException(ex);
            throw;
        }
        finally
        {
            decoder?.Dispose();
            if (source != 0)
            {
                OpenAlNative.AlSourceStop(source);
                OpenAlNative.AlDeleteSources(1, ref source);
            }
            if (buffers[0] != 0)
                OpenAlNative.AlDeleteBuffers(BufferCount, buffers);
            OpenAlNative.AlcMakeContextCurrent(nint.Zero);
            OpenAlNative.AlcDestroyContext(_context);
            OpenAlNative.AlcCloseDevice(_device);
        }
    }

    private async Task PumpAlsaAsync()
    {
        FfmpegPcmDecoder? decoder = null;
        var decodeBuffer = new byte[BufferSize];
        var writeTrackIndex = 0;
        long writtenFrames = 0;

        try
        {
            decoder = await CreateAlsaDecoderAsync(0, TimeSpan.Zero, _cts.Token).ConfigureAwait(false);
            _started.TrySetResult();

            while (!_cts.IsCancellationRequested)
            {
                ApplyAlsaControlRequests(ref decoder, ref writeTrackIndex, ref writtenFrames);
                if (_paused)
                {
                    Interlocked.Exchange(
                        ref _playedFrames,
                        Math.Max(0, writtenFrames - AlsaNative.GetDelay(_alsaPcm)));
                    UpdateAudibleTrack();
                    await Task.Delay(5, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                var bytesRead = await decoder!.ReadAsync(decodeBuffer, _cts.Token).ConfigureAwait(false);
                bytesRead -= bytesRead % 8;
                if (bytesRead == 0)
                {
                    decoder.Dispose();
                    decoder = null;
                    var nextIndex = writeTrackIndex + 1;
                    if (_stopAfterCurrentTrack || nextIndex >= _items.Count)
                        break;

                    writeTrackIndex = nextIndex;
                    Volatile.Write(ref _trackStartFrames[nextIndex], writtenFrames);
                    var nextInfo = _items[nextIndex].TryCreateKnownAudioInfo()
                                   ?? await ProbeAsync(_items[nextIndex].PlaybackPath, _cts.Token).ConfigureAwait(false);
                    var nextRate = ResolveAlsaTrackRate(nextInfo);
                    if (nextRate != _sampleRate)
                        SwitchAlsaRate(nextRate, writtenFrames);
                    _infos[nextIndex] = ApplyKnownDuration(_items[nextIndex], nextInfo)
                        with { OutputSampleRate = _sampleRate };
                    decoder = await CreateAlsaDecoderAsync(nextIndex, TimeSpan.Zero, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                ProcessPcm32(
                    decodeBuffer.AsSpan(0, bytesRead),
                    _items[writeTrackIndex].ReplayGainFactor,
                    _volume);
                var byteOffset = 0;
                while (byteOffset < bytesRead && !_cts.IsCancellationRequested)
                {
                    var frames = AlsaNative.Write(
                        _alsaPcm,
                        decodeBuffer.AsSpan(byteOffset, bytesRead - byteOffset));
                    if (frames == 0)
                        continue;
                    writtenFrames += frames;
                    byteOffset += frames * 8;
                    Interlocked.Exchange(
                        ref _playedFrames,
                        Math.Max(0, writtenFrames - AlsaNative.GetDelay(_alsaPcm)));
                    UpdateAudibleTrack();
                }
            }

            if (!_cts.IsCancellationRequested)
            {
                AlsaNative.Drain(_alsaPcm);
                Interlocked.Exchange(ref _playedFrames, writtenFrames);
                UpdateAudibleTrack();
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            _started.TrySetCanceled(_cts.Token);
        }
        catch (Exception ex)
        {
            _started.TrySetException(ex);
            throw;
        }
        finally
        {
            decoder?.Dispose();
            AlsaNative.Drop(_alsaPcm);
            AlsaNative.Close(_alsaPcm);
        }
    }

    private void ApplyAlsaControlRequests(
        ref FfmpegPcmDecoder? decoder,
        ref int writeTrackIndex,
        ref long writtenFrames)
    {
        SeekRequest? seek;
        lock (_requestLock)
        {
            seek = _pendingSeek;
            _pendingSeek = null;
        }
        if (seek is null)
            return;

        try
        {
            AlsaNative.Drop(_alsaPcm);
            var prepareResult = AlsaNative.Prepare(_alsaPcm);
            if (prepareResult < 0)
                throw new IOException(LocalizationManager.Current.AlsaPrepareFailed);
            decoder?.Dispose();
            writeTrackIndex = Volatile.Read(ref _audibleTrackIndex);
            decoder = CreateAlsaDecoderAsync(writeTrackIndex, seek.Position, _cts.Token)
                .GetAwaiter().GetResult();
            writtenFrames = (long)(seek.Position.TotalSeconds * _sampleRate);
            Volatile.Write(ref _trackStartFrames[writeTrackIndex], 0);
            Interlocked.Exchange(ref _playedFrames, writtenFrames);
            _equalizer.Reset();
            seek.Completion.TrySetResult();
        }
        catch (Exception ex)
        {
            seek.Completion.TrySetException(ex);
        }
    }

    private void ApplyControlRequests(
        uint source,
        ref FfmpegPcmDecoder? decoder,
        ref int writeTrackIndex,
        ref long completedFrames,
        Queue<uint> freeBuffers,
        Dictionary<uint, int> queuedFrames)
    {
        SeekRequest? seek;
        lock (_requestLock)
        {
            seek = _pendingSeek;
            _pendingSeek = null;
        }
        if (seek is null)
            return;

        try
        {
            OpenAlNative.AlSourceStop(source);
            OpenAlNative.AlGetSourcei(source, OpenAlNative.AlBuffersQueued, out var queued);
            while (queued-- > 0)
            {
                OpenAlNative.AlSourceUnqueueBuffers(source, 1, out var buffer);
                freeBuffers.Enqueue(buffer);
            }
            queuedFrames.Clear();
            decoder?.Dispose();
            writeTrackIndex = Volatile.Read(ref _audibleTrackIndex);
            decoder = CreateDecoderAsync(writeTrackIndex, seek.Position, _cts.Token)
                .GetAwaiter().GetResult();
            completedFrames = (long)(seek.Position.TotalSeconds * _sampleRate);
            Volatile.Write(ref _trackStartFrames[writeTrackIndex], 0);
            Interlocked.Exchange(ref _playedFrames, completedFrames);
            _equalizer.Reset();
            seek.Completion.TrySetResult();
        }
        catch (Exception ex)
        {
            seek.Completion.TrySetException(ex);
        }
    }

    private async Task<FfmpegPcmDecoder> CreateDecoderAsync(
        int index,
        TimeSpan position,
        CancellationToken cancellationToken) =>
        await FfmpegPcmDecoder.CreateAsync(
            _items[index].PlaybackPaths,
            _sampleRate,
            "s16le",
            "pcm_s16le",
            position,
            _items[index].SegmentStart,
            _items[index].SegmentEnd,
            cancellationToken).ConfigureAwait(false);

    private async Task<FfmpegPcmDecoder> CreateAlsaDecoderAsync(
        int index,
        TimeSpan position,
        CancellationToken cancellationToken) =>
        await FfmpegPcmDecoder.CreateAsync(
            _items[index].PlaybackPaths,
            _sampleRate,
            "s32le",
            "pcm_s32le",
            position,
            _items[index].SegmentStart,
            _items[index].SegmentEnd,
            cancellationToken).ConfigureAwait(false);

    private void UpdateAudibleTrack()
    {
        var playedFrames = Interlocked.Read(ref _playedFrames);
        while (_audibleTrackIndex + 1 < _infos.Length &&
               _infos[_audibleTrackIndex + 1] is not null &&
               playedFrames >= Volatile.Read(ref _trackStartFrames[_audibleTrackIndex + 1]))
        {
            _audibleTrackIndex++;
            ReplayGainFactor = _items[_audibleTrackIndex].ReplayGainFactor;
            TrackChanged?.Invoke(
                this,
                new GaplessTrackChangedEventArgs(
                    _items[_audibleTrackIndex].FilePath,
                    _infos[_audibleTrackIndex]!));
        }
    }

    private void ProcessPcm16(Span<byte> bytes, float trackReplayGain)
    {
        var update = Interlocked.Exchange(ref _pendingEqualizerUpdate, null);
        if (update is not null)
            _equalizer.Update(update.Enabled, update.Profile);
        var samples = MemoryMarshal.Cast<byte, short>(bytes);
        for (var index = 0; index + 1 < samples.Length; index += 2)
        {
            var output = _equalizer.Process(
                samples[index] / 32768.0f * trackReplayGain,
                samples[index + 1] / 32768.0f * trackReplayGain);
            samples[index] = FloatToInt16(output.Left);
            samples[index + 1] = FloatToInt16(output.Right);
        }
    }

    private void ProcessPcm32(Span<byte> bytes, float trackReplayGain, float volume)
    {
        var update = Interlocked.Exchange(ref _pendingEqualizerUpdate, null);
        if (update is not null)
        {
            _equalizerEnabled = update.Enabled;
            _equalizerProfile = update.Profile;
            _equalizer.Update(update.Enabled, update.Profile);
        }
        var gain = trackReplayGain * volume * volume * volume;
        var samples = MemoryMarshal.Cast<byte, int>(bytes);
        for (var index = 0; index + 1 < samples.Length; index += 2)
        {
            var output = _equalizer.Process(
                samples[index] / 2147483648.0f * gain,
                samples[index + 1] / 2147483648.0f * gain);
            samples[index] = FloatToInt32(output.Left);
            samples[index + 1] = FloatToInt32(output.Right);
        }
    }

    private static short FloatToInt16(float sample) =>
        (short)Math.Round(Math.Clamp(sample, -1.0f, 1.0f) * (sample < 0 ? 32768.0f : 32767.0f));

    private static int FloatToInt32(float sample) =>
        sample <= -1.0f
            ? int.MinValue
            : sample >= 1.0f
                ? int.MaxValue
                : (int)Math.Round(sample * 2147483647.0);

    /// <summary>Resolves the direct-ALSA output rate for one probed track.</summary>
    /// <param name="info">Probed or pre-known track information.</param>
    /// <returns>The exact output rate, capped by the configured maximum.</returns>
    private int ResolveAlsaTrackRate(AudioFileInfo info)
    {
        var rate = info.IsDsd ? DsdPlaybackOptions.ResolveOutputRate() : NormalizePcmRate(info.OutputSampleRate);
        return _maxOutputSampleRateHz > 0 ? Math.Min(rate, _maxOutputSampleRateHz) : rate;
    }

    /// <summary>Reopens the direct-ALSA device at a new source rate at a track boundary.</summary>
    /// <param name="newRate">Required output rate in hertz.</param>
    /// <param name="writtenFrames">Cumulative frames written before the switch.</param>
    private void SwitchAlsaRate(int newRate, long writtenFrames)
    {
        var previousRate = _sampleRate;
        AlsaNative.Drain(_alsaPcm);
        AlsaNative.Close(_alsaPcm);
        _alsaPcm = AlsaNative.OpenExact(_alsaDeviceName, newRate);
        _sampleRate = newRate;
        SeekDiagnostics.Log("alsa-rate-switch", $"from={previousRate} to={newRate}");
        _equalizer = new ParametricEqualizer(newRate, _equalizerEnabled, _equalizerProfile);
        Interlocked.Exchange(ref _playedFrames, writtenFrames);
    }

    private static AudioFileInfo ApplyKnownDuration(GaplessPlaybackItem item, AudioFileInfo info) =>
        item.SegmentDuration is { } segmentDuration
            ? info with { Duration = segmentDuration }
            : item.KnownDuration is { } knownDuration
                ? info with { Duration = knownDuration }
                : info;

    private static async Task<AudioFileInfo> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        var isHttp = filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var options = isHttp
            ? "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 2 -analyzeduration 500000 -probesize 500000 "
            : string.Empty;
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error {options}-select_streams a:0 -show_entries stream=codec_name,sample_rate,channels,duration -of json \"{filePath}\"",
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("ffprobe konnte nicht gestartet werden.");
        var json = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var stream = document.RootElement.GetProperty("streams")[0];
        var codec = stream.GetProperty("codec_name").GetString() ?? "unknown";
        var rate = int.Parse(stream.GetProperty("sample_rate").GetString() ?? "0", CultureInfo.InvariantCulture);
        var channels = stream.GetProperty("channels").GetInt32();
        var duration = stream.TryGetProperty("duration", out var durationJson) &&
                       double.TryParse(durationJson.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : 0;
        var isDsd = codec.Contains("dsd", StringComparison.OrdinalIgnoreCase);
        return new AudioFileInfo(
            codec,
            rate,
            channels,
            isDsd ? DsdPlaybackOptions.ResolveOutputRate() : NormalizePcmRate(rate),
            isDsd,
            Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
            TimeSpan.FromSeconds(duration));
    }

    private static int NormalizePcmRate(int sampleRate) =>
        sampleRate is >= 8_000 and <= 768_000 ? sampleRate : 192_000;

    private sealed record EqualizerUpdateRequest(bool Enabled, EqualizerProfile? Profile);

    private sealed record SeekRequest(TimeSpan Position)
    {
        internal TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
