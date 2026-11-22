using System.Collections.ObjectModel;
using System.Globalization;
using AlUmmahNow.ToolsCore.Import;
using LogLevel = AlUmmahNow.ToolsCore.Import.LogLevel;
using AlUmmahNow.VisualImporterUno.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace AlUmmahNow.VisualImporterUno.ViewModels;

/// <summary>
/// The Import page (wiki TOOLS-ADMIN TA-11): pick or make a channel, scan a folder, review what was found, put it in
/// the work queue, and follow the queue.
/// </summary>
public sealed partial class BulkImportViewModel : ObservableObject, IDisposable
{
    /// <summary>Spinners turn, except in demo frames, where they are drawn as a still, full ring (a ring caught mid-arc reads as "(" or «ر»).</summary>
    public static bool SpinnersMove => !(ImportWorkspace.IfStarted?.IsDemo ?? false);
    public bool SpinnerMovesHere => SpinnersMove;

    private readonly ImportWorkspace _workspace;
    private readonly DispatcherQueue? _dispatcher;
    private readonly Dictionary<string, string> _channelNames = new();
    private CancellationTokenSource? _search;
    private bool _attached;
    private bool _disposed;

    public BulkImportViewModel()
    {
        _workspace = ImportWorkspace.Current;
        _dispatcher = App.Current?.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        foreach (var c in DemoImport.Channels.Where(_ => _workspace.IsDemo)) _channelNames[c.Id] = c.Name;
        Attach();
        var settings = _workspace.Queue.Settings;
        _hashParallel = settings.ParallelFor(ImportStepKind.Hash) - 1;
        _uploadParallel = settings.ParallelFor(ImportStepKind.Upload) - 1;
        _attachParallel = settings.ParallelFor(ImportStepKind.Attach) - 1;
        if (_workspace.IsDemo && !DemoImport.Empty)
        {
            Channels.Clear();
            foreach (var c in DemoImport.Channels) Channels.Add(c);
            SelectedChannel = Channels[0];
            FolderPath = DemoImport.Folder;
            CaptionLanguageIndex = 1;
            ShowScan(DemoImport.Scan());
        }
    }

    // ---------------------------------------------------------------- Channel

    public ObservableCollection<ImportChannel> Channels { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChannel), nameof(ChannelSummary))]
    [NotifyCanExecuteChangedFor(nameof(AddToQueueCommand))]
    private ImportChannel? _selectedChannel;

    [ObservableProperty]
    private string _channelError = "";

    [ObservableProperty]
    private bool _isSearchingChannels;

    public bool HasChannel => SelectedChannel is not null;

    public string ChannelSummary => SelectedChannel is null
        ? ""
        : string.Join("\n", new[] { SelectedChannel.Handle is { Length: > 0 } h ? "@" + h : null, SelectedChannel.OwnerEmail }.Where(t => !string.IsNullOrEmpty(t)));

    /// <summary>Looks channels up by name or handle, a moment after typing stops.</summary>
    public async Task SearchChannelsAsync(string query)
    {
        _search?.Cancel();
        var cts = _search = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, cts.Token);
            IsSearchingChannels = true;
            var found = await _workspace.Api.ListChannelsAsync(query, 1, cts.Token);
            if (cts.IsCancellationRequested) return;
            Channels.Clear();
            foreach (var c in found)
            {
                Channels.Add(c);
                _channelNames[c.Id] = c.Name;
            }
            ChannelError = found.Count == 0 && !string.IsNullOrWhiteSpace(query) ? Loc.Get("Import_ChannelNone") : "";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ChannelError = ErrorText(ex);
        }
        finally
        {
            if (_search == cts) IsSearchingChannels = false;
        }
    }

    /// <summary>Makes a channel owned by the signed-in admin and picks it; answers the error to show, or null.</summary>
    public async Task<string?> CreateChannelAsync(string name, string? handle, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) return Loc.Get("Import_NewChannelNameNeeded");
        try
        {
            var channel = await _workspace.Api.CreateChannelAsync(name.Trim(), string.IsNullOrWhiteSpace(handle) ? null : handle.Trim().TrimStart('@'), string.IsNullOrWhiteSpace(description) ? null : description.Trim());
            _channelNames[channel.Id] = channel.Name;
            Channels.Insert(0, channel);
            SelectedChannel = channel;
            ChannelError = "";
            return null;
        }
        catch (ImportApiException ex) when (ex.Status == 409)
        {
            return Loc.Get("Import_NewChannelTaken");
        }
        catch (Exception ex)
        {
            return ErrorText(ex);
        }
    }

    // ---------------------------------------------------------------- Folder and review

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string _folderPath = "";

    [ObservableProperty]
    private bool _includeSubfolders;

    /// <summary>The language of caption files named without one: none, Arabic or English</summary>
    [ObservableProperty]
    private int _captionLanguageIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReview), nameof(ShowEmptyReview))]
    private int _reviewCount;

    public ObservableCollection<ReviewRow> ReviewRows { get; } = new();

    public bool HasReview => ReviewCount > 0;

    public bool ShowEmptyReview => !HasReview && QueueRows.Count == 0;

    [ObservableProperty]
    private string _reviewSummary = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToQueueCommand))]
    private int _selectedCount;

    [ObservableProperty]
    private string _addButtonText = "";

    [ObservableProperty]
    private bool _isAdding;

    private bool CanScan() => !IsScanning && !string.IsNullOrWhiteSpace(FolderPath);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        ScanMessage = Loc.Get("Import_Scanning");
        var folder = FolderPath.Trim();
        var subfolders = IncludeSubfolders;
        var language = CaptionLanguageIndex switch { 1 => "ar", 2 => "en", _ => null };
        try
        {
            if (!Directory.Exists(folder))
            {
                ScanMessage = Loc.Get("Import_FolderMissing");
                return;
            }
            var result = await Task.Run(() => FolderScanner.Scan(folder, subfolders, language));
            ShowScan(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScanMessage = Loc.GetWithDetail("Import_ScanFailed", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void ShowScan(ScanResult result)
    {
        ReviewRows.Clear();
        // A file already in the queue is not offered again
        var queued = _workspace.Queue.Items.Select(i => i.Video.Path).ToHashSet(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var video in result.Videos)
        {
            if (queued.Contains(video.Path))
            {
                skipped++;
                continue;
            }
            var row = new ReviewRow(video);
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ReviewRow.IsSelected)) CountSelected(); };
            ReviewRows.Add(row);
        }
        ReviewCount = ReviewRows.Count;
        CountSelected();
        var size = ReviewRows.Sum(r => r.Video.Size);
        ReviewSummary = ReviewRows.Count == 0 ? "" : Loc.Format("Import_FoundSummary", Loc.Plural("Import_VideoCount", ReviewRows.Count), Loc.Bytes(size));
        var lines = new List<string>();
        if (result.Videos.Count == 0) lines.Add(Loc.Get("Import_NoVideos"));
        if (skipped > 0) lines.Add(Loc.Plural("Import_AlreadyQueued", skipped));
        lines.AddRange(result.Notes.Select(NoteText));
        ScanMessage = string.Join("\n", lines);
        OnPropertyChanged(nameof(ShowEmptyReview));
    }

    private void CountSelected()
    {
        SelectedCount = ReviewRows.Count(r => r.IsSelected);
        AddButtonText = Loc.Plural("Import_AddToQueue", SelectedCount);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var r in ReviewRows) r.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var r in ReviewRows) r.IsSelected = false;
    }

    private bool CanAdd() => SelectedChannel is not null && SelectedCount > 0 && !IsAdding;

    /// <summary>A batch on admin-api for the folder, then the chosen files into the queue.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddToQueueAsync()
    {
        var channel = SelectedChannel!;
        var chosen = ReviewRows.Where(r => r.IsSelected).ToList();
        IsAdding = true;
        AddToQueueCommand.NotifyCanExecuteChanged();
        try
        {
            var name = Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var batch = await _workspace.Api.CreateBatchAsync(channel.Id, string.IsNullOrWhiteSpace(name) ? null : name);
            _channelNames[channel.Id] = channel.Name;
            var now = DateTimeOffset.UtcNow;
            _workspace.Queue.Add(chosen.Select((r, i) => QueueItem.For(r.Video, batch.Id, channel.Id, now.AddMilliseconds(i))));
            foreach (var r in chosen) ReviewRows.Remove(r);
            ReviewCount = ReviewRows.Count;
            CountSelected();
            ScanMessage = Loc.WithDetail(Loc.Plural("Import_Added", chosen.Count), channel.Name);
        }
        catch (Exception ex)
        {
            ScanMessage = ErrorText(ex);
        }
        finally
        {
            IsAdding = false;
            AddToQueueCommand.NotifyCanExecuteChanged();
        }
    }

    // ---------------------------------------------------------------- Queue

    public ObservableCollection<QueueRow> QueueRows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQueue))]
    private int _queueCount;

    public bool HasQueue => QueueCount > 0;

    [ObservableProperty]
    private string _queueSummary = "";

    [ObservableProperty]
    private bool _allPaused;

    [ObservableProperty]
    private bool _hasFinished;

    /// <summary>The choices for how many run at once, written per language (1 to 8; uploads 1 to 5).</summary>
    public IReadOnlyList<string> ParallelChoices { get; } = Enumerable.Range(1, 8).Select(n => Loc.Number(n)).ToList();

    public IReadOnlyList<string> UploadChoices { get; } = Enumerable.Range(1, QueueSettings.MaxUploads).Select(n => Loc.Number(n)).ToList();

    // Picker positions: the count less one
    [ObservableProperty]
    private int _hashParallel;

    [ObservableProperty]
    private int _uploadParallel;

    [ObservableProperty]
    private int _attachParallel;

    partial void OnHashParallelChanged(int value) => SetParallel(ImportStepKind.Hash, value);
    partial void OnUploadParallelChanged(int value) => SetParallel(ImportStepKind.Upload, value);
    partial void OnAttachParallelChanged(int value) => SetParallel(ImportStepKind.Attach, value);

    private void SetParallel(ImportStepKind kind, int index)
    {
        if (index < 0) return;
        _workspace.Queue.SetParallel(kind, index + 1);
    }

    [RelayCommand]
    private void PauseAll() => _workspace.Queue.PauseAll();

    [RelayCommand]
    private void ResumeAll() => _workspace.Queue.ResumeAll();

    [RelayCommand]
    private void ClearFinished() => _workspace.Queue.ClearDone();

    public void Pause(QueueRow row) => _workspace.Queue.Pause(row.Id);
    public void Resume(QueueRow row) => _workspace.Queue.Resume(row.Id);
    public void Retry(QueueRow row) => _workspace.Queue.Retry(row.Id);
    public void Remove(QueueRow row) => _workspace.Queue.Remove(row.Id);

    /// <summary>Shows or hides the file's log under its row.</summary>
    public void ToggleLog(QueueRow row)
    {
        row.ShowLog = !row.ShowLog;
        if (row.ShowLog) row.SetLog(_workspace.Queue.LogLines(row.Id));
    }

    /// <summary>The file's log as plain text for a bug report: the file, then one line per entry.</summary>
    public string LogTextFor(QueueRow row) =>
        row.FileName + "\n" + string.Join("\n", _workspace.Queue.LogLines(row.Id).Select(l => $"{Loc.TimeWithZone(l.At.LocalDateTime)}  {LogText(l).Replace("\n", " — ")}"));

    private void OnQueueChanged(string? id)
    {
        if (_disposed) return;
        if (_dispatcher is null || _dispatcher.HasThreadAccess) SyncQueue();
        else _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, SyncQueue);
    }

    private void SyncQueue()
    {
        if (_disposed) return;
        var items = _workspace.Queue.Items;
        var byId = QueueRows.ToDictionary(r => r.Id);
        for (var i = QueueRows.Count - 1; i >= 0; i--)
        {
            if (items.All(it => it.Id != QueueRows[i].Id)) QueueRows.RemoveAt(i);
        }
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!byId.TryGetValue(item.Id, out var row))
            {
                row = new QueueRow(item.Id, item.Video.FileName, TitleFor(item.Video.Metadata));
                QueueRows.Insert(Math.Min(i, QueueRows.Count), row);
            }
            row.Update(item, _channelNames.TryGetValue(item.ChannelId, out var name) ? name : null);
            if (row.ShowLog) row.SetLog(_workspace.Queue.LogLines(item.Id));
        }
        QueueCount = QueueRows.Count;
        AllPaused = _workspace.Queue.AllPaused;
        HasFinished = items.Any(i => i.Phase == ItemPhase.Done);
        QueueSummary = Summary(items);
        OnPropertyChanged(nameof(ShowEmptyReview));
    }

    private static string Summary(IReadOnlyList<QueueItem> items)
    {
        if (items.Count == 0) return "";
        var parts = new List<string>();
        void Add(ItemPhase phase, string key)
        {
            var n = items.Count(i => i.Phase == phase);
            if (n > 0) parts.Add(Loc.Plural(key, n));
        }
        Add(ItemPhase.Done, "Import_SumDone");
        Add(ItemPhase.Running, "Import_SumRunning");
        Add(ItemPhase.Waiting, "Import_SumWaiting");
        Add(ItemPhase.RetryWaiting, "Import_SumRetrying");
        Add(ItemPhase.Paused, "Import_SumPaused");
        Add(ItemPhase.Failed, "Import_SumFailed");
        return string.Join(Loc.IsArabic ? "، " : " · ", parts);
    }

    // ---------------------------------------------------------------- Words

    internal static string NoteText(ScanNote note)
    {
        var text = Loc.Get("ImportNote_" + note.Code);
        // A note that names its file inside the sentence ({0}); the others, and Arabic (a Latin name cannot sit inside an
        // Arabic line in Uno's text), take it on its own line after a colon
        return text.Contains("{0}") ? text.Replace("{0}", note.Detail ?? "") : Loc.WithDetail(text, note.Detail);
    }

    /// <summary>A failure in the app's words; admin-api's own words follow when it explained itself.</summary>
    internal static string ErrorText(string? code, string? raw)
    {
        if (code is null) return raw ?? "";
        var text = Loc.Get("ImportError_" + code);
        if (string.IsNullOrEmpty(text) || text == "ImportError_" + code) text = Loc.Get("ImportError_unexpected");
        return code is "server" or "upload_refused" or "unexpected" ? Loc.WithDetail(text, raw) : text;
    }

    internal static string ErrorText(Exception ex) => ErrorText(ImportQueue.ErrorCode(ex), ex.Message);

    internal static string StepName(string kind) => Loc.Get("ImportStep_" + kind);

    /// <summary>A log line in the app's words, from its code and arguments; the English text when the code is new.</summary>
    internal static string LogText(LogLine line)
    {
        var a = line.Args ?? Array.Empty<string>();
        string Arg(int i) => i < a.Length ? a[i] : "";
        string Num(int i) => long.TryParse(Arg(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Loc.Number(n) : Arg(i);
        return line.Code switch
        {
            // A file name, hash, id or reference goes on its own line: Uno only reverses the Arabic runs of a mixed line
            "added" or "sha" or "upload_opened" or "upload_reopened" or "uploaded" or "already_imported" => Loc.WithDetail(Loc.Get("ImportLog_" + line.Code), Arg(0)),
            "paused" or "resumed" or "finished" or "session_ended" => Loc.Get("ImportLog_" + line.Code),
            "report_failed" => Loc.WithDetail(Loc.Get("ImportLog_report_failed"), ErrorText(Arg(0), Arg(1))),
            "retry" or "started" or "stopped" or "done" or "skipped" => Loc.Format("ImportLog_" + line.Code, StepName(Arg(0))),
            "attempt" => Loc.Format("ImportLog_attempt", StepName(Arg(0)), Num(1)),
            "attempt_failed" => Loc.WithDetail(Loc.Format("ImportLog_attempt_failed", StepName(Arg(0)), Num(1), Num(3)), ErrorText(Arg(2), Arg(4))),
            "failed" => Loc.WithDetail(Loc.WithDetail(Loc.Format("ImportLog_failed", StepName(Arg(0)), Num(1)), ErrorText(Arg(2), Arg(4))), Loc.Format("ImportLog_reference", Arg(3))),
            "sent" => Loc.Format("ImportLog_sent", string.Join(Loc.IsArabic ? "، " : ", ", a.Skip(1).Select(Part))),
            "left_out_size" => Loc.WithDetail(Loc.Format("ImportLog_left_out_size", Num(1)), Arg(0)),
            "left_out_unreadable" => Loc.WithDetail(Loc.WithDetail(Loc.Get("ImportLog_left_out_unreadable"), Arg(0)), Arg(1)),
            _ => line.Text,
        };
    }

    private static string Part(string part) => part.StartsWith("captions=", StringComparison.Ordinal)
        ? Loc.Format("ImportPart_captions", string.Join(Loc.IsArabic ? "، " : ", ", part["captions=".Length..].Split(',').Select(LanguageName)))
        : Loc.Get("ImportPart_" + part);

    internal static string TitleFor(ImportMetadata metadata) =>
        Loc.IsArabic && !string.IsNullOrWhiteSpace(metadata.TitleAr) ? metadata.TitleAr! : metadata.Title;

    internal static string LanguageName(string code) => code switch
    {
        "ar" => Loc.Get("Import_LanguageArabic"),
        "en" => Loc.Get("Import_LanguageEnglish"),
        _ => code,
    };

    /// <summary>Follows the queue while the page is shown.</summary>
    public void Attach()
    {
        if (_attached || _disposed) return;
        _attached = true;
        _workspace.Queue.Changed += OnQueueChanged;
        SyncQueue();
    }

    /// <summary>Stops following the queue when the page goes away; the queue itself carries on.</summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _search?.Cancel();
        _workspace.Queue.Changed -= OnQueueChanged;
    }

    public void Dispose()
    {
        Detach();
        _disposed = true;
    }
}

/// <summary>A video the scan found, in the review table.</summary>
public sealed partial class ReviewRow : ObservableObject
{
    public ReviewRow(ScannedVideo video)
    {
        Video = video;
        _isSelected = !video.Notes.Any(n => n.Code == ScanNoteCode.TooLarge);
        var parts = new List<string>();
        if (video.Captions.Count > 0)
        {
            parts.Add(Loc.Format("ImportPart_captions", string.Join(Loc.IsArabic ? "، " : ", ", video.Captions.Select(c => BulkImportViewModel.LanguageName(c.Language)))));
        }
        if (video.ChaptersPath is not null) parts.Add(Loc.Get("ImportPart_chapters"));
        if (video.ThumbnailPath is not null) parts.Add(Loc.Get("ImportPart_thumbnail"));
        if (video.MetadataPath is not null) parts.Add(Loc.Get("ImportPart_metadata"));
        Sidecars = Loc.KeepLastWords(parts.Count == 0 ? Loc.Get("Import_NoSidecars") : string.Join(Loc.IsArabic ? "، " : "\u00A0· ", parts));
        Notes = Loc.KeepLastWords(string.Join("\n", video.Notes.Select(BulkImportViewModel.NoteText)));
    }

    public ScannedVideo Video { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string FileName => Video.FileName;

    /// <summary>The title in the app's language when the file has one; the other language's under it.</summary>
    public string Title => BulkImportViewModel.TitleFor(Video.Metadata);

    public string OtherTitle => Video.Metadata.Title is var en && en != Title ? en
        : Video.Metadata.TitleAr is { Length: > 0 } ar && ar != Title ? ar : "";

    public bool HasOtherTitle => OtherTitle.Length > 0;

    public string TitleShown => Loc.RightToLeftLine(Title);
    public string OtherTitleShown => Loc.RightToLeftLine(OtherTitle);
    public string Size => Loc.Bytes(Video.Size);
    public string Sidecars { get; }
    public string Notes { get; }
    public bool HasNotes => Notes.Length > 0;
}

/// <summary>One event in a file's log: its time and its text, a line each (a file name, hash or reference has its own).</summary>
public sealed record LogEntry(string Time, IReadOnlyList<string> Lines, bool IsProblem)
{
    public string Text => string.Join("\n", Lines);
}

/// <summary>A file in the queue, as the queue's table shows it.</summary>
public sealed partial class QueueRow : ObservableObject
{
    public QueueRow(string id, string fileName, string title)
    {
        Id = id;
        FileName = fileName;
        Title = title;
    }

    public string Id { get; }
    public string FileName { get; }
    public string Title { get; }
    public string TitleShown => Loc.RightToLeftLine(Title);

    /// <summary>False in demo frames: the ring is drawn whole and still, never caught mid-arc where it reads as a letter.</summary>
    public bool SpinnerMoves => BulkImportViewModel.SpinnersMove;

    [ObservableProperty] private string _channel = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private bool _isWaiting;
    [ObservableProperty] private bool _isRetrying;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _canPause;
    [ObservableProperty] private bool _canResume;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty] private bool _showLog;
    [ObservableProperty] private bool _logCopied;

    /// <summary>The log under the row while it is open: one entry per event, each line of it on its own.</summary>
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    internal void SetLog(IReadOnlyList<LogLine> lines)
    {
        if (LogEntries.Count == lines.Count) return;
        for (var i = LogEntries.Count; i < lines.Count; i++)
        {
            var line = lines[i];
            LogEntries.Add(new LogEntry(Loc.TimeWithZone(line.At.LocalDateTime), BulkImportViewModel.LogText(line).Split('\n'), line.Level != LogLevel.Info));
        }
    }

    internal void Update(QueueItem item, string? channel)
    {
        Channel = channel ?? "";
        var step = item.CurrentStep;
        var phase = item.Phase;
        IsDone = phase == ItemPhase.Done;
        IsFailed = phase == ItemPhase.Failed;
        IsWaiting = phase == ItemPhase.Waiting;
        IsRetrying = phase == ItemPhase.RetryWaiting;
        IsPaused = phase == ItemPhase.Paused;
        ShowProgress = phase == ItemPhase.Running;
        Progress = Math.Round(item.Progress * 100, 1);
        CanPause = phase is ItemPhase.Waiting or ItemPhase.Running or ItemPhase.RetryWaiting;
        CanResume = phase == ItemPhase.Paused;
        CanRetry = phase is ItemPhase.Failed or ItemPhase.RetryWaiting;
        Status = phase switch
        {
            ItemPhase.Done => item.AlreadyImported ? Loc.Get("ImportPhase_AlreadyImported") : Loc.Get("ImportPhase_Done"),
            ItemPhase.Running when step!.Kind == ImportStepKind.Upload => Loc.Format("ImportPhase_Uploading", Loc.Percent(item.Progress * 100)),
            ItemPhase.Running => Loc.Get("ImportPhase_Running_" + step!.Kind),
            ItemPhase.RetryWaiting => Loc.Format("ImportPhase_RetryAt", Loc.TimeWithZone(step!.NextAttemptAt!.Value.LocalDateTime)),
            ItemPhase.Paused => Loc.Get("ImportPhase_Paused"),
            ItemPhase.Failed => Loc.Format("ImportPhase_Failed", BulkImportViewModel.StepName(step!.Kind.ToString())),
            _ => Loc.Format("ImportPhase_Waiting", BulkImportViewModel.StepName(step!.Kind.ToString())),
        };
        Detail = phase is ItemPhase.Failed or ItemPhase.RetryWaiting ? BulkImportViewModel.ErrorText(step!.LastErrorCode, step.LastError) : "";
        HasDetail = Detail.Length > 0;
    }
}
