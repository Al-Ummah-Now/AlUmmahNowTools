using AlUmmahNow.Core;

namespace AlUmmahNow.ToolsCore.Transcription;

/// <summary>A whisper model the app offers (aun-core's catalog), with its download size.</summary>
public sealed record SpeechModel(string Id, long SizeBytes);

/// <summary>Where whisper runs: the GPU (Metal, Vulkan) or the CPU (aun-core <c>transcription_device()</c>).</summary>
public sealed record SpeechDevice(bool Gpu, string Backend, string Name);

/// <summary>Speech to text on this computer (TA-13): whisper.cpp inside aun-core, the model downloaded once.</summary>
public interface ITranscriber
{
    /// <summary>False when this build of aun-core has no on-device transcription.</summary>
    bool Available { get; }
    /// <summary>The device whisper runs on, or null without on-device transcription.</summary>
    SpeechDevice? Device { get; }
    /// <summary>The models the admin can choose, most accurate first (empty without on-device transcription).</summary>
    IReadOnlyList<SpeechModel> Models { get; }
    /// <summary>The installed model's file, or null.</summary>
    string? InstalledPath(string modelId);
    /// <summary>Downloads and checks the model (resumable; one download at a time). Progress 0–1.</summary>
    Task<string> DownloadAsync(string modelId, IProgress<double> progress, CancellationToken ct);
    /// <summary>aun-core's transcript JSON. <paramref name="language"/> is ISO 639-1, or null to detect it. Progress 0–1.</summary>
    Task<string> TranscribeAsync(string modelPath, string mediaPath, string? language, IProgress<double> progress, CancellationToken ct);
}

/// <summary>A local transcription failure: not the file's failure (the file is imported without a transcript).</summary>
public sealed class TranscribeException : Exception
{
    public TranscribeException(string code, string message, Exception? inner = null) : base(message, inner) => Code = code;

    /// <summary>unavailable, model_download, decode, transcribe, disk</summary>
    public string Code { get; }
}

/// <summary>aun-core's whisper.cpp: Metal on macOS, Vulkan on Windows and Linux, the CPU otherwise.</summary>
public sealed class CoreTranscriber : ITranscriber
{
    /// <summary>The spec's default (TOOLS-ADMIN TA-13), then smaller ones for slower computers.</summary>
    public const string DefaultModel = "large-v3-turbo";

    public static readonly IReadOnlyList<string> Offered = ["large-v3-turbo", "large-v3-turbo-q5_0", "medium-q5_0", "small-q5_1", "base-q5_1"];

    private readonly ModelStore _store;
    private readonly SemaphoreSlim _download = new(1, 1);
    private readonly Lazy<SpeechDevice?> _device;

    public CoreTranscriber(string modelsDirectory)
    {
        _store = new ModelStore(modelsDirectory);
        _device = new Lazy<SpeechDevice?>(() =>
        {
            var d = AunCoreMethods.TranscriptionDevice();
            return d is null ? null : new SpeechDevice(d.Gpu, d.Backend, d.Name);
        });
    }

    public static string DefaultModelsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNowTools", "models");

    /// <summary>The offered models in aun-core's catalog, in the offered order.</summary>
    public IReadOnlyList<SpeechModel> Models => _models.Value;

    private readonly Lazy<IReadOnlyList<SpeechModel>> _models = new(() =>
    {
        var catalog = AunCoreMethods.TranscriptionModelCatalog().ToDictionary(m => m.Id);
        return Offered.Where(catalog.ContainsKey).Select(id => new SpeechModel(id, (long)catalog[id].SizeBytes)).ToList();
    });

    public bool Available => AunCoreMethods.TranscriptionOnDeviceAvailable();

    public SpeechDevice? Device => _device.Value;

    public string? InstalledPath(string modelId)
    {
        try
        {
            return _store.Path(modelId);
        }
        catch (AunException)
        {
            return null;
        }
    }

    public async Task<string> DownloadAsync(string modelId, IProgress<double> progress, CancellationToken ct)
    {
        await _download.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (InstalledPath(modelId) is { } path) return path;
            using var cancel = new MediaCancel();
            using var registration = ct.Register(cancel.Cancel);
            try
            {
                return await _store.Download(modelId, new Progress(progress), cancel).ConfigureAwait(false);
            }
            catch (AunException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (AunException ex)
            {
                throw new TranscribeException("model_download", ex.Message, ex);
            }
        }
        finally
        {
            _download.Release();
        }
    }

    public async Task<string> TranscribeAsync(string modelPath, string mediaPath, string? language, IProgress<double> progress, CancellationToken ct)
    {
        using var cancel = new MediaCancel();
        using var registration = ct.Register(cancel.Cancel);
        try
        {
            return await AunCoreMethods.TranscribeOnDeviceJson(modelPath: modelPath, mediaPath: mediaPath, language: language, progress: new Progress(progress), cancel: cancel).ConfigureAwait(false);
        }
        catch (AunException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (AunException ex)
        {
            throw new TranscribeException("transcribe", ex.Message, ex);
        }
    }

    /// <summary>aun-core's progress callback (bytes for downloads, percent for transcription) as 0–1.</summary>
    private sealed class Progress(IProgress<double> target) : MediaProgress
    {
        public void OnProgress(ulong done, ulong total) => target.Report(total == 0 ? 0 : Math.Clamp((double)done / total, 0, 1));
    }
}
