using System.Collections.ObjectModel;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Social;
using AlUmmahNow.VisualImporterUno.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace AlUmmahNow.VisualImporterUno.ViewModels;

/// <summary>
/// The Social sources page (wiki TOOLS-ADMIN TA-13, on the TA-9 API): a super admin lists a channel's sources (or every
/// channel's), adds one from an account the owner connected or with the owner's recorded permission, runs a sync, changes
/// its schedule, turns it off or removes it, and follows each sync job: its state, attempts, next try, reason, log and
/// Retry. The sync itself is a queued, idempotent admin job on the server (retry with backoff, then the DLQ and the
/// Errors page); the app only asks for it and shows it.
/// </summary>
public sealed partial class SocialSourcesViewModel : ObservableObject, IDisposable
{
    /// <summary>The admin-api permission for every source call: super admins only.</summary>
    public const string Permission = "import_sources:manage";

    /// <summary>How often the page reads the sources again while a sync is waiting or running.</summary>
    private static readonly TimeSpan ActivePoll = TimeSpan.FromSeconds(10);

    private readonly ImportWorkspace _workspace;
    private readonly DispatcherQueue? _dispatcher;
    private readonly Dictionary<string, string> _channelNames = new();
    private readonly Dictionary<string, SyncJob> _jobs = new();
    private CancellationTokenSource? _search;
    private CancellationTokenSource? _poll;
    private bool _disposed;

    public SocialSourcesViewModel() : this(ImportWorkspace.Current)
    {
    }

    internal SocialSourcesViewModel(ImportWorkspace? workspace)
    {
        _workspace = workspace!;
        _dispatcher = workspace is null ? null : App.Current?.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        var isDemo = workspace?.IsDemo == true;
        foreach (var c in DemoImport.Channels.Where(_ => isDemo)) _channelNames[c.Id] = c.Name;
        NetworkChoices = SourceNetworks.All.Select(NetworkName).ToArray();
        ScheduleChoices = SourceSchedules.All.Select(ScheduleName).ToArray();
        _scheduleIndex = SourceSchedules.All.ToList().IndexOf(SourceSchedules.Daily);
        UpdateNetworkHelp();
        if (isDemo && DemoSocial.Channel is { } channel)
        {
            Channels.Add(channel);
            _selectedChannel = channel;
            DemoSocial.FillForm(this);
        }
    }

    /// <summary>Spinners are drawn still in demo frames.</summary>
    public bool SpinnerMovesHere => BulkImportViewModel.SpinnersMove;

    /// <summary>Only super admins manage sources; anyone else sees why the page is empty (admin-api refuses them too).</summary>
    public bool IsSuperAdmin => ShellState.Current.Session.Profile?.Can(Permission) ?? false;

    public bool IsNotSuperAdmin => !IsSuperAdmin;

    // ---------------------------------------------------------------- Channel

    public ObservableCollection<ImportChannel> Channels { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChannel), nameof(ChannelSummary), nameof(ShowAddForm), nameof(ShowPickChannel))]
    private ImportChannel? _selectedChannel;

    [ObservableProperty]
    private string _channelError = "";

    public bool HasChannel => SelectedChannel is not null;

    public string ChannelSummary => SelectedChannel is null
        ? ""
        : string.Join("\n", new[] { SelectedChannel.Handle is { Length: > 0 } h ? "@" + h : null, SelectedChannel.OwnerEmail }.Where(t => !string.IsNullOrEmpty(t)));

    public async Task SearchChannelsAsync(string query)
    {
        _search?.Cancel();
        var cts = _search = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token).ConfigureAwait(true);
            var found = await _workspace.Api.ListChannelsAsync(query, 1, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested) return;
            Channels.Clear();
            foreach (var c in found)
            {
                Channels.Add(c);
                _channelNames[c.Id] = c.Name;
            }
            ChannelError = found.Count == 0 && query.Trim().Length > 0 ? Loc.Get("Social_NoChannelFound") : "";
        }
        catch (OperationCanceledException)
        {
        }
        catch (ImportApiException ex)
        {
            ChannelError = ErrorText(ex);
        }
    }

    partial void OnSelectedChannelChanged(ImportChannel? value)
    {
        if (value is not null) _channelNames[value.Id] = value.Name;
        FormError = "";
        _ = RefreshAsync();
    }

    /// <summary>Back to every channel's sources.</summary>
    [RelayCommand]
    private void ShowAllChannels() => SelectedChannel = null;

    // ---------------------------------------------------------------- Sources

    public ObservableCollection<SourceRow> Sources { get; } = new();

    [ObservableProperty] private string _sourcesSummary = "";
    [ObservableProperty] private bool _hasSources;
    [ObservableProperty] private bool _showNoSources;
    [ObservableProperty] private string _noSourcesBody = "";

    /// <summary>
    /// The upload switch (Features page): a sync and a Takeout file's upload make new posts and videos, so while it is off
    /// Sync now, Retry on sync jobs, and Upload matched files are not offered (no text says why; admins see it on
    /// the Features page). Read with the list, and set off by a <c>404 feature_off</c> answer.
    /// </summary>
    [ObservableProperty] private bool _uploadsOn = true;

    private DateTimeOffset _featuresReadAt = DateTimeOffset.MinValue;
    private string? _addedSourceLabel;

    partial void OnUploadsOnChanged(bool value)
    {
        foreach (var row in Sources) row.UploadsOn = value;
        if (_addedSourceLabel is { } label && FormMessage.Length > 0) FormMessage = AddedMessage(label, value);
    }

    internal void SetAddedSource(string? label)
    {
        _addedSourceLabel = label;
        FormMessage = label is { Length: > 0 } ? AddedMessage(label, UploadsOn) : "";
    }

    private async Task ReadUploadsSwitchAsync()
    {
        if (DateTimeOffset.UtcNow - _featuresReadAt < TimeSpan.FromSeconds(60)) return;
        _featuresReadAt = DateTimeOffset.UtcNow;
        if (await _workspace.Api.IsFeatureOnAsync("upload").ConfigureAwait(true) is bool on) UploadsOn = on;
    }

    /// <summary>A refusal because the upload switch is off: the actions go, and nothing is shown.</summary>
    private bool UploadsWentOff(ImportApiException ex)
    {
        if (!ex.IsFeatureOff) return false;
        UploadsOn = false;
        return true;
    }
    [ObservableProperty] private string _listError = "";
    [ObservableProperty] private bool _isLoading;

    public bool HasListError => ListError.Length > 0;

    partial void OnListErrorChanged(string value) => OnPropertyChanged(nameof(HasListError));

    /// <summary>Reads the sources (of the chosen channel, or all), their latest jobs and, for a channel, its connections.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!IsSuperAdmin || _disposed) return;
        IsLoading = true;
        var channel = SelectedChannel;
        try
        {
            var sources = await _workspace.Api.ListSourcesAsync(channel?.Id).ConfigureAwait(true);
            if (!ReferenceEquals(channel, SelectedChannel)) return;
            await ReadUploadsSwitchAsync().ConfigureAwait(true);
            await LoadJobsAsync(sources).ConfigureAwait(true);
            await LoadChannelNamesAsync(sources).ConfigureAwait(true);
            ShowSources(sources);
            ListError = "";
            if (channel is not null) await LoadConnectionsAsync(channel).ConfigureAwait(true);
            else Connections.Clear();
            UpdateConnections();
        }
        catch (ImportApiException ex)
        {
            ListError = ErrorText(ex);
            Report(ex, "list");
        }
        finally
        {
            IsLoading = false;
            SchedulePoll();
        }
    }

    private async Task LoadJobsAsync(IReadOnlyList<ImportSource> sources)
    {
        foreach (var source in sources)
        {
            if (source.LastJobId is not { } jobId) continue;
            // A finished job never changes again: read it once
            if (_jobs.TryGetValue(jobId, out var known) && !known.IsActive && !known.CanRetry) continue;
            try
            {
                _jobs[jobId] = await _workspace.Api.GetJobAsync(jobId).ConfigureAwait(true);
            }
            catch (ImportApiException ex) when (ex.Status == 404)
            {
                _jobs.Remove(jobId);
            }
        }
    }

    private async Task LoadChannelNamesAsync(IReadOnlyList<ImportSource> sources)
    {
        foreach (var id in sources.Select(s => s.ChannelId).Distinct().Where(id => !_channelNames.ContainsKey(id)))
        {
            try
            {
                _channelNames[id] = await _workspace.Api.GetChannelNameAsync(id).ConfigureAwait(true) ?? id;
            }
            catch (ImportApiException)
            {
                // The id stands in for a name the API could not give
                _channelNames[id] = id;
            }
        }
    }

    private void ShowSources(IReadOnlyList<ImportSource> sources)
    {
        var showChannel = SelectedChannel is null;
        var keep = Sources.ToDictionary(r => r.Id);
        Sources.Clear();
        foreach (var source in sources)
        {
            var row = keep.TryGetValue(source.Id, out var existing) ? existing : new SourceRow(source.Id);
            var job = source.LastJobId is { } id && _jobs.TryGetValue(id, out var j) ? j : null;
            row.UploadsOn = UploadsOn;
            row.Update(source, job, showChannel ? _channelNames.GetValueOrDefault(source.ChannelId, source.ChannelId) : null);
            Sources.Add(row);
        }
        HasSources = Sources.Count > 0;
        ShowNoSources = !HasSources;
        NoSourcesBody = Loc.Get(SelectedChannel is null ? "Social_NoSourcesAnyBody" : "Social_NoSourcesBody");
        var attention = Sources.Count(r => r.IsFailed || r.IsOffNotConnected);
        var parts = new List<string> { Loc.Plural("Social_SourceCount", Sources.Count) };
        if (attention > 0) parts.Add(Loc.Format("Social_AttentionCount", attention));
        SourcesSummary = HasSources ? Loc.Commas(parts) : "";
    }

    /// <summary>While a sync waits or runs, the page reads the sources again every few seconds; never in demo frames.</summary>
    private void SchedulePoll()
    {
        _poll?.Cancel();
        if (_disposed || _workspace.IsDemo || !Sources.Any(r => r.IsSyncing || r.IsRetrying)) return;
        var cts = _poll = new CancellationTokenSource();
        _ = Task.Delay(ActivePoll, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) _dispatcher?.TryEnqueue(() => _ = RefreshAsync());
        }, TaskScheduler.Default);
    }

    // ---------------------------------------------------------------- Row actions

    public async Task SyncAsync(SourceRow row) => await Act(row, "sync", async () =>
    {
        var started = await _workspace.Api.SyncSourceAsync(row.Id).ConfigureAwait(true);
        _jobs.Remove(started.JobId);
    }).ConfigureAwait(true);

    public Task SetEnabledAsync(SourceRow row, bool enabled) => Act(row, enabled ? "turn_on" : "turn_off", () => _workspace.Api.UpdateSourceAsync(row.Id, enabled: enabled));

    public Task SetScheduleAsync(SourceRow row, int index) =>
        index < 0 || index >= SourceSchedules.All.Count ? Task.CompletedTask : Act(row, "schedule", () => _workspace.Api.UpdateSourceAsync(row.Id, schedule: SourceSchedules.All[index]));

    public Task RemoveAsync(SourceRow row) => Act(row, "remove", () => _workspace.Api.RemoveSourceAsync(row.Id));

    public Task RetryJobAsync(SourceRow row) => row.JobId is not { } jobId ? Task.CompletedTask : Act(row, "retry", async () =>
    {
        // A cancelled sync starts a new run (Sync now); a failed one runs again
        if (row.JobIsCancelled) await _workspace.Api.SyncSourceAsync(row.Id).ConfigureAwait(true);
        else await _workspace.Api.RetryJobAsync(jobId).ConfigureAwait(true);
        _jobs.Remove(jobId);
    });

    /// <summary>
    /// Cancels the source's waiting or running sync (the page asks first). The server stops it between items; it ends as
    /// cancelled, not as a failure, with a log line naming the admin. Idempotent: a sync that finished meanwhile is shown as
    /// it is.
    /// </summary>
    public Task CancelJobAsync(SourceRow row) => row.JobId is not { } jobId ? Task.CompletedTask : Act(row, "cancel", async () =>
    {
        var job = await _workspace.Api.CancelSourceJobAsync(row.Id, jobId).ConfigureAwait(true);
        _jobs[job.Id] = job;
        row.ShowJob = true;
    });

    /// <summary>Runs one action on a source, then reads the list again; a refusal shows under the source.</summary>
    private async Task Act(SourceRow row, string action, Func<Task> call)
    {
        if (row.IsBusy) return;
        row.IsBusy = true;
        row.ActionError = "";
        try
        {
            await call().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (ImportApiException ex) when (UploadsWentOff(ex))
        {
            // Uploads were switched off meanwhile: Sync now and Retry are no longer offered
        }
        catch (ImportApiException ex)
        {
            row.ActionError = Loc.KeepLastWords(ErrorText(ex));
            Report(ex, action, row.Id);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    /// <summary>Opens or closes the source's sync job under its row.</summary>
    public void ToggleJob(SourceRow row)
    {
        row.ShowJob = !row.ShowJob;
        if (row.ShowJob) row.ShowItems = false;
    }

    /// <summary>Opens or closes the source's latest items under its row, reading them when it opens.</summary>
    public async Task ToggleItemsAsync(SourceRow row)
    {
        row.ShowItems = !row.ShowItems;
        if (!row.ShowItems) return;
        row.ShowJob = false;
        row.ItemsMessage = Loc.Get("Social_ItemsLoading");
        try
        {
            var items = await _workspace.Api.ListSourceItemsAsync(row.Id, null, 50).ConfigureAwait(true);
            row.SetItems(items);
        }
        catch (ImportApiException ex)
        {
            row.ItemsMessage = ErrorText(ex);
            Report(ex, "items", row.Id);
        }
    }

    // ---------------------------------------------------------------- YouTube Takeout files

    /// <summary>How often the Takeout panel reads the match again while its job waits or runs.</summary>
    private static readonly TimeSpan TakeoutPoll = TimeSpan.FromSeconds(3);

    /// <summary>Opens or closes a YouTube source's Takeout panel, reading what was listed before when it opens.</summary>
    public async Task ToggleTakeoutAsync(SourceRow row)
    {
        row.ShowTakeout = !row.ShowTakeout;
        if (!row.ShowTakeout) return;
        row.ShowJob = false;
        row.ShowItems = false;
        await LoadTakeoutAsync(row).ConfigureAwait(true);
    }

    private async Task LoadTakeoutAsync(SourceRow row)
    {
        try
        {
            var report = await _workspace.Api.GetTakeoutAsync(row.Id).ConfigureAwait(true);
            SyncJob? job = null;
            // A finished match job is no longer the source's (the server releases it): the last one this page saw says how it ended
            if (report.JobId is { } current) row.LastTakeoutJobId = current;
            if ((report.JobId ?? row.LastTakeoutJobId) is { } jobId)
            {
                try
                {
                    job = await _workspace.Api.GetJobAsync(jobId).ConfigureAwait(true);
                }
                catch (ImportApiException ex) when (ex.Status == 404)
                {
                }
            }
            row.SetTakeout(report, job);
        }
        catch (ImportApiException ex)
        {
            row.TakeoutError = ErrorText(ex);
            Report(ex, "takeout", row.Id);
        }
    }

    /// <summary>
    /// Reads the Takeout export in <paramref name="folder"/> (its videos and <c>videos.csv</c>), lists the files on the server
    /// (in requests of at most 500) and follows the match job until it is done.
    /// </summary>
    public async Task ListTakeoutFolderAsync(SourceRow row, string folder)
    {
        if (row.TakeoutBusy) return;
        row.TakeoutBusy = true;
        row.TakeoutError = "";
        try
        {
            var files = _workspace.IsDemo ? DemoSocial.TakeoutFolder() : await Task.Run(() => TakeoutExport.Read(folder)).ConfigureAwait(true);
            row.RememberTakeoutPaths(files);
            if (files.Count == 0)
            {
                row.TakeoutError = Loc.GetWithDetail("Takeout_NoVideos", folder);
                return;
            }
            row.TakeoutMessage = Loc.Format("Takeout_Listing", files.Count);
            foreach (var chunk in files.Chunk(TakeoutExport.MaxPerRequest))
            {
                var listed = await _workspace.Api.ListTakeoutFilesAsync(row.Id, chunk).ConfigureAwait(true);
                row.LastTakeoutJobId = listed.JobId;
            }
            await FollowTakeoutAsync(row).ConfigureAwait(true);
        }
        catch (ImportApiException ex)
        {
            row.TakeoutError = Loc.KeepLastWords(ErrorText(ex));
            Report(ex, "takeout_list", row.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            row.TakeoutError = Loc.GetWithDetail("Takeout_FolderFailed", ex.Message);
        }
        finally
        {
            row.TakeoutMessage = "";
            row.TakeoutBusy = false;
        }
    }

    /// <summary>Reads the match again every few seconds while its job waits or runs (at most a few minutes; Refresh reads it later).</summary>
    private async Task FollowTakeoutAsync(SourceRow row)
    {
        for (var i = 0; i < 100 && !_disposed; i++)
        {
            await LoadTakeoutAsync(row).ConfigureAwait(true);
            if (!row.TakeoutJobActive || _workspace.IsDemo) return;
            await Task.Delay(TakeoutPoll).ConfigureAwait(true);
        }
    }

    /// <summary>The admin's pick for an ambiguous file.</summary>
    public async Task ConfirmTakeoutAsync(SourceRow row, TakeoutFileRow file, TakeoutCandidateRow candidate)
    {
        if (file.IsBusy) return;
        file.IsBusy = true;
        file.Error = "";
        try
        {
            await _workspace.Api.ConfirmTakeoutFileAsync(row.Id, file.Id, candidate.ItemId).ConfigureAwait(true);
            await LoadTakeoutAsync(row).ConfigureAwait(true);
        }
        catch (ImportApiException ex)
        {
            file.Error = Loc.KeepLastWords(ErrorText(ex));
            Report(ex, "takeout_confirm", row.Id);
        }
        finally
        {
            file.IsBusy = false;
        }
    }

    /// <summary>Sends every matched file the app has on this computer, one at a time; the server's job then posts each video.</summary>
    public async Task UploadTakeoutAsync(SourceRow row)
    {
        if (row.TakeoutBusy) return;
        row.TakeoutBusy = true;
        row.TakeoutError = "";
        try
        {
            foreach (var file in row.TakeoutFiles.Where(f => f.CanUpload).ToList())
            {
                if (row.TakeoutPathOf(file) is not { } path) continue;
                file.Progress = Loc.Format("Takeout_Uploading", Loc.Percent(0));
                try
                {
                    await _workspace.Takeout.UploadAsync(row.Id, file.File, path, new Progress<double>(p => file.Progress = Loc.Format("Takeout_Uploading", Loc.Percent(p * 100))), CancellationToken.None).ConfigureAwait(true);
                    file.Progress = "";
                }
                catch (ImportApiException ex) when (UploadsWentOff(ex))
                {
                    // Uploads were switched off: the rest wait, and Upload matched files is no longer offered
                    file.Progress = "";
                    break;
                }
                catch (ImportApiException ex)
                {
                    file.Progress = "";
                    file.Error = Loc.KeepLastWords(ErrorText(ex));
                    Report(ex, "takeout_upload", row.Id);
                }
                catch (TakeoutUploadException ex)
                {
                    file.Progress = "";
                    file.Error = Loc.KeepLastWords(Loc.Get("Takeout_Error_" + ex.Code));
                }
                catch (TusGoneException)
                {
                    file.Progress = "";
                    file.Error = Loc.KeepLastWords(Loc.Get("Takeout_Error_upload_gone"));
                }
                catch (Exception ex) when (ex is TusException or IOException or InvalidOperationException or HttpRequestException)
                {
                    file.Progress = "";
                    file.Error = Loc.GetWithDetail("Takeout_UploadFailed", ex.Message);
                }
            }
            await FollowTakeoutAsync(row).ConfigureAwait(true);
        }
        finally
        {
            row.TakeoutBusy = false;
        }
    }

    /// <summary>Cancels the Takeout match job (asked first, as a sync).</summary>
    public async Task CancelTakeoutJobAsync(SourceRow row)
    {
        if (row.TakeoutJobId is not { } jobId || row.TakeoutBusy) return;
        try
        {
            await _workspace.Api.CancelSourceJobAsync(row.Id, jobId).ConfigureAwait(true);
            await LoadTakeoutAsync(row).ConfigureAwait(true);
        }
        catch (ImportApiException ex)
        {
            row.TakeoutError = Loc.KeepLastWords(ErrorText(ex));
            Report(ex, "takeout_cancel", row.Id);
        }
    }

    public string JobLogTextFor(SourceRow row) =>
        row.Label + "\n" + row.JobStatus + "\n" + string.Join("\n", row.JobEntries.Select(e => $"{e.Time}  {e.Text.Replace("\n", " — ")}"));

    // ---------------------------------------------------------------- Connections

    public ObservableCollection<ConnectionRow> Connections { get; } = new();

    [ObservableProperty] private bool _hasConnections;
    [ObservableProperty] private bool _showNoConnections;

    private async Task LoadConnectionsAsync(ImportChannel channel)
    {
        var found = await _workspace.Api.ListConnectionsAsync(channel.Id).ConfigureAwait(true);
        if (!ReferenceEquals(channel, SelectedChannel)) return;
        Connections.Clear();
        foreach (var c in found) Connections.Add(new ConnectionRow(c));
    }

    private void UpdateConnections()
    {
        foreach (var c in Connections)
        {
            c.IsAdded = Sources.Any(s => s.Network == c.Connection.Network && s.Address == c.Connection.Address);
        }
        HasConnections = Connections.Count > 0;
        ShowNoConnections = HasChannel && Connections.Count == 0;
    }

    /// <summary>Adds a connected account as a source, with the schedule chosen in the form; no permission is needed.</summary>
    public async Task AddConnectionAsync(ConnectionRow row)
    {
        if (SelectedChannel is not { } channel || row.IsAdded || IsAdding) return;
        var c = row.Connection;
        await SendNewSourceAsync(new NewSource(channel.Id, c.Network, c.Address, null, SelectedSchedule, null)).ConfigureAwait(true);
    }

    // ---------------------------------------------------------------- Add form

    public string[] NetworkChoices { get; }
    public string[] ScheduleChoices { get; }

    [ObservableProperty] private int _networkIndex;
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private int _scheduleIndex;
    [ObservableProperty] private string _permissionBy = "";
    [ObservableProperty] private string _permissionNote = "";
    [ObservableProperty] private string _formError = "";
    [ObservableProperty] private string _formMessage = "";
    [ObservableProperty] private string _addressHint = "";
    [ObservableProperty] private bool _showPermission = true;
    [ObservableProperty] private bool _showTelegramNote;
    [ObservableProperty] private bool _showAddressBox = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private bool _isAdding;

    public bool ShowAddForm => IsSuperAdmin && HasChannel;
    public bool ShowPickChannel => IsSuperAdmin && !HasChannel;
    public bool HasFormError => FormError.Length > 0;
    public bool HasFormMessage => FormMessage.Length > 0;

    partial void OnFormErrorChanged(string value) => OnPropertyChanged(nameof(HasFormError));
    partial void OnFormMessageChanged(string value) => OnPropertyChanged(nameof(HasFormMessage));
    partial void OnNetworkIndexChanged(int value) => UpdateNetworkHelp();

    private string SelectedNetwork => SourceNetworks.All[Math.Clamp(NetworkIndex, 0, SourceNetworks.All.Count - 1)];
    private string SelectedSchedule => SourceSchedules.All[Math.Clamp(ScheduleIndex, 0, SourceSchedules.All.Count - 1)];

    private void UpdateNetworkHelp()
    {
        var network = SelectedNetwork;
        AddressHint = network switch
        {
            SourceNetworks.Bluesky => Loc.GetWithDetail("Social_Hint_bluesky", "name.bsky.social"),
            SourceNetworks.Mastodon => Loc.GetWithDetail("Social_Hint_mastodon", "@name@mastodon.social"),
            SourceNetworks.YouTube => Loc.GetWithDetail("Social_Hint_youtube", "UCq3Zx9LmT4Kd7Vn2Wp8RbYs"),
            SourceNetworks.Rss => Loc.GetWithDetail("Social_Hint_rss", "https://alummahnow.com/feed.xml"),
            _ => "",
        };
        // Telegram is added only from the owner's connection (our bot reads the posts)
        ShowTelegramNote = SourceNetworks.ConnectedOnly(network);
        ShowAddressBox = !ShowTelegramNote;
        ShowPermission = !ShowTelegramNote;
        FormError = "";
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var (request, problem) = SourceForm.Build(SelectedChannel?.Id, SelectedNetwork, Address, Label, SelectedSchedule, PermissionBy, PermissionNote,
            Connections.Select(c => c.Connection).ToList());
        if (problem is not null)
        {
            FormError = Loc.KeepLastWords(Loc.Get("SocialError_" + problem));
            return;
        }
        if (await SendNewSourceAsync(request!).ConfigureAwait(true))
        {
            Address = "";
            Label = "";
            PermissionBy = "";
            PermissionNote = "";
        }
    }

    private bool CanAdd() => !IsAdding;

    private async Task<bool> SendNewSourceAsync(NewSource request)
    {
        IsAdding = true;
        FormError = "";
        SetAddedSource(null);
        try
        {
            var source = await _workspace.Api.AddSourceAsync(request).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SetAddedSource(source.Label);
            return true;
        }
        catch (ImportApiException ex)
        {
            FormError = Loc.KeepLastWords(ErrorText(ex));
            Report(ex, "add");
            return false;
        }
        finally
        {
            IsAdding = false;
        }
    }

    // ---------------------------------------------------------------- Words

    public static string NetworkName(string network) => Loc.Get("SocialNetwork_" + network);

    public static string ScheduleName(string schedule) => Loc.Get("SocialSchedule_" + schedule);

    /// <summary>The message shown after a source is added (drops "Sync it now" while uploads are off).</summary>
    internal static string AddedMessage(string label, bool uploadsOn) =>
        Loc.GetWithDetail(uploadsOn ? "Social_Added" : "Social_AddedQuiet", label);

    /// <summary>Lines for the Cancel sync dialog (omits "You can start a new sync later" while uploads are off).</summary>
    internal static string[] CancelDialogLines(string label, bool uploadsOn) =>
        uploadsOn
            ? new[] { label, Loc.Get("Social_CancelBody"), Loc.Get("Social_CancelBody2") }
            : new[] { label, Loc.Get("Social_CancelBody") };

    /// <summary>A time with its date when not today and the device's zone: "Tomorrow, 2:20 PM EST" / «غدًا، ٢:٢٠ م غرينتش-٥».</summary>
    public static string When(DateTimeOffset at) => Loc.When(at.LocalDateTime) + " " + Loc.ZoneLabel(at.LocalDateTime);

    /// <summary>
    /// A refused or failed call in the app's words: the codes the sources API answers, else the generic text with the
    /// server's own words on their own line.
    /// </summary>
    internal static string ErrorText(ImportApiException ex)
    {
        var key = ex.Status == 0 ? "SocialError_unreachable" : "SocialError_" + ex.Code;
        var known = Loc.Get(key);
        if (known != key) return known;
        // A code the app doesn't know: the server's own sentence, already in the admin's language (X-App-Locale)
        if (ex.ServerMessage is { } said) return said;
        return ex.Status >= 500 ? Loc.Get("SocialError_server") : Loc.GetWithDetail("SocialError_refused", ex.Message);
    }

    /// <summary>A failure that is not the admin's to fix (no answer, a server error) goes to the Errors page too.</summary>
    private void Report(ImportApiException ex, string action, string? sourceId = null)
    {
        if (!ex.IsRetryable || _workspace.IsDemo) return;
        _workspace.Reporter.Report("caught", ex, $"Social sources: {action} failed: {ex.Message}", "/social-sources", new Dictionary<string, object?>
        {
            ["action"] = action,
            ["code"] = ex.Code,
            ["status"] = ex.Status,
            ["sourceId"] = sourceId,
            ["channelId"] = SelectedChannel?.Id,
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _poll?.Cancel();
        _search?.Cancel();
    }
}

/// <summary>One source as the page shows it.</summary>
public sealed partial class SourceRow : ObservableObject
{
    public SourceRow(string id)
    {
        Id = id;
        ScheduleChoices = SourceSchedules.All.Select(SocialSourcesViewModel.ScheduleName).ToArray();
    }

    public string Id { get; }
    public string[] ScheduleChoices { get; }
    public bool SpinnerMoves => BulkImportViewModel.SpinnersMove;

    public string Network { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string? JobId { get; private set; }

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _networkLine = "";
    [ObservableProperty] private string _channelLine = "";
    [ObservableProperty] private bool _hasChannelLine;
    [ObservableProperty] private string _authLine = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string? _statusTip;
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private string _lastSync = "";
    [ObservableProperty] private string? _lastSyncTip;
    [ObservableProperty] private string _counts = "";
    [ObservableProperty] private bool _hasCounts;
    [ObservableProperty] private int _scheduleIndex;
    [ObservableProperty] private bool _isOk;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private bool _isRetrying;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private bool _isOff;
    [ObservableProperty] private bool _isOffNotConnected;
    [ObservableProperty] private bool _canSync;

    /// <summary>The upload switch (see <see cref="SocialSourcesViewModel.UploadsOn"/>): Sync now, sync job Retry, and Takeout uploads need it.</summary>
    [ObservableProperty] private bool _uploadsOn = true;

    private bool _syncable;
    private bool _retryable;

    private (ImportSource Source, SyncJob? Job, string? Channel)? _shown;
    private (TakeoutReport Report, SyncJob? Job)? _takeoutShown;

    partial void OnUploadsOnChanged(bool value)
    {
        // Status, failed job, and Takeout rows drop "Sync now", "Retry", and "ready to upload" while uploads are off
        if (_shown is { } shown) Update(shown.Source, shown.Job, shown.Channel);
        if (_takeoutShown is { } takeout) UpdateTakeoutJob(takeout.Job);
        foreach (var file in TakeoutFiles) file.SetUploadsOn(value);
        UpdateUploadable();
    }

    /// <summary>The last Takeout match job this page saw (the server releases a finished one; its end is still shown).</summary>
    public string? LastTakeoutJobId { get; set; }
    [ObservableProperty] private bool _canTurnOff;
    [ObservableProperty] private bool _canTurnOn;
    [ObservableProperty] private bool _canChangeSchedule;
    [ObservableProperty] private bool _hasJob;
    [ObservableProperty] private bool _showJob;
    [ObservableProperty] private bool _showItems;
    [ObservableProperty] private string _jobStatus = "";
    [ObservableProperty] private string? _jobStatusTip;
    [ObservableProperty] private string _jobDetail = "";
    [ObservableProperty] private bool _hasJobDetail;
    [ObservableProperty] private bool _canRetryJob;
    [ObservableProperty] private bool _canCancelJob;
    [ObservableProperty] private bool _isCancelled;
    [ObservableProperty] private bool _isYouTube;
    [ObservableProperty] private bool _showTakeout;
    [ObservableProperty] private string _takeoutSummary = "";
    [ObservableProperty] private bool _hasTakeoutSummary;
    [ObservableProperty] private string _takeoutJobStatus = "";
    [ObservableProperty] private bool _hasTakeoutJob;
    [ObservableProperty] private bool _takeoutJobActive;
    [ObservableProperty] private string _takeoutMessage = "";
    [ObservableProperty] private bool _hasTakeoutMessage;
    [ObservableProperty] private string _takeoutError = "";
    [ObservableProperty] private bool _hasTakeoutError;
    [ObservableProperty] private bool _canUploadTakeout;
    [ObservableProperty] private string _uploadTakeoutHint = "";
    [ObservableProperty] private bool _hasUploadTakeoutHint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TakeoutIdle))]
    private bool _takeoutBusy;

    public bool TakeoutIdle => !TakeoutBusy;
    public string? TakeoutJobId { get; private set; }
    public bool JobIsCancelled { get; private set; }
    public ObservableCollection<TakeoutFileRow> TakeoutFiles { get; } = new();
    private readonly Dictionary<(string, long), string> _takeoutPaths = new();

    partial void OnTakeoutMessageChanged(string value) => HasTakeoutMessage = value.Length > 0;
    partial void OnTakeoutErrorChanged(string value) => HasTakeoutError = value.Length > 0;
    partial void OnTakeoutBusyChanged(bool value) => UpdateUploadable();

    /// <summary>Where each listed file is on this computer (by name and size, as the server keys it).</summary>
    internal void RememberTakeoutPaths(IEnumerable<TakeoutFileInput> files)
    {
        foreach (var f in files.Where(f => f.Path is not null)) _takeoutPaths[(f.Name, f.Size)] = f.Path!;
    }

    internal string? TakeoutPathOf(TakeoutFileRow file) => _takeoutPaths.GetValueOrDefault((file.File.Name, file.File.Size));

    internal void SetTakeout(TakeoutReport report, SyncJob? job)
    {
        _takeoutShown = (report, job);
        TakeoutError = "";
        TakeoutJobId = report.JobId;
        TakeoutFiles.Clear();
        foreach (var file in report.Files.OrderBy(f => TakeoutStates.Order.ToList().IndexOf(f.State)).ThenBy(f => f.Name, StringComparer.Ordinal))
        {
            TakeoutFiles.Add(new TakeoutFileRow(file, UploadsOn));
        }
        var parts = new List<string>();
        foreach (var state in TakeoutStates.Order)
        {
            var n = report.Count(state);
            if (n > 0) parts.Add(Loc.Format("Takeout_Count_" + state, n).Replace(' ', '\u00A0'));
        }
        TakeoutSummary = Loc.Commas(parts);
        HasTakeoutSummary = parts.Count > 0;
        UpdateTakeoutJob(job);
        UpdateUploadable();
    }

    private void UpdateTakeoutJob(SyncJob? job)
    {
        HasTakeoutJob = job is not null;
        TakeoutJobActive = job?.IsActive ?? false;
        TakeoutJobStatus = job is null ? "" : Loc.KeepLastWords(job.Status switch
        {
            "queued" or "running" => Loc.Get("TakeoutJob_matching"),
            "retrying" => Loc.Get("TakeoutJob_retrying"),
            "failed" => Loc.WithDetail(Loc.Get(UploadsOn ? "TakeoutJob_failed" : "TakeoutJob_failedQuiet"), job.LastError is { } e ? ReasonText(e, labelled: false, uploadsOn: UploadsOn) : null),
            "cancelled" => job.CancelledBy is { Length: > 0 } by ? Loc.GetWithDetail("TakeoutJob_cancelledBy", by) : Loc.Get("TakeoutJob_cancelled"),
            _ => Loc.Get("TakeoutJob_done"),
        });
    }

    private void UpdateUploadable()
    {
        var sendable = TakeoutFiles.Count(f => f.CanUpload && TakeoutPathOf(f) is not null);
        var waiting = TakeoutFiles.Count(f => f.CanUpload);
        CanUploadTakeout = sendable > 0 && !TakeoutBusy && UploadsOn;
        // Matched files listed from another computer (or before the app restarted): choose the folder again to send them (only when uploads are on)
        UploadTakeoutHint = UploadsOn && waiting > sendable ? Loc.Get("Takeout_ChooseAgainToUpload") : "";
        HasUploadTakeoutHint = UploadTakeoutHint.Length > 0;
    }

    /// <summary>"Cancelled by {name}" with the admin's name on its own line, or "Cancelled by an admin".</summary>
    internal static string CancelledLine(SyncJob job) =>
        job.CancelledBy is { Length: > 0 } name ? Loc.GetWithDetail("SocialJob_cancelledBy", name) : Loc.Get("SocialJob_cancelled");
    [ObservableProperty] private bool _jobCopied;
    [ObservableProperty] private string _itemsMessage = "";
    [ObservableProperty] private bool _hasItemsMessage;
    [ObservableProperty] private string _actionError = "";
    [ObservableProperty] private bool _hasActionError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    public ObservableCollection<LogEntry> JobEntries { get; } = new();
    public ObservableCollection<SourceItemRow> Items { get; } = new();

    partial void OnActionErrorChanged(string value) => HasActionError = value.Length > 0;
    partial void OnItemsMessageChanged(string value) => HasItemsMessage = value.Length > 0;

    internal void Update(ImportSource source, SyncJob? job, string? channelName)
    {
        _shown = (source, job, channelName);
        Network = source.Network;
        Address = source.Address;
        JobId = source.LastJobId;
        Label = source.Label;
        // The network's name in the admin's language; the address, Latin, on its own line
        NetworkLine = Loc.WithDetail(SocialSourcesViewModel.NetworkName(source.Network), source.Address);
        ChannelLine = channelName is null ? "" : Loc.GetWithDetail("Social_Channel", channelName);
        HasChannelLine = ChannelLine.Length > 0;
        AuthLine = source.IsConnected
            ? Loc.Get("Social_AuthConnected")
            : Loc.WithDetail(Loc.WithDetail(source.Permission?.At is { } at ? Loc.Format("Social_AuthRecordedAt", SocialSourcesViewModel.When(at)) : Loc.Get("Social_AuthRecorded"), source.Permission?.By), source.Permission?.Note);
        ScheduleIndex = Math.Max(0, SourceSchedules.All.ToList().IndexOf(source.Schedule));

        var phase = SourceText.PhaseOf(source, job);
        IsOk = phase is SourcePhase.Manual or SourcePhase.Scheduled;
        IsSyncing = phase == SourcePhase.Syncing;
        IsRetrying = phase == SourcePhase.Retrying;
        IsFailed = phase == SourcePhase.Failed;
        IsOff = phase == SourcePhase.Off;
        IsOffNotConnected = phase == SourcePhase.OffNotConnected;
        IsCancelled = phase == SourcePhase.Cancelled;
        IsOk = IsOk || IsCancelled;
        IsYouTube = source.Network == SourceNetworks.YouTube;
        _syncable = source.Enabled && !IsSyncing;
        CanSync = _syncable && UploadsOn;
        CanTurnOff = source.Enabled;
        CanTurnOn = !source.Enabled;
        CanChangeSchedule = source.Enabled;
        var retryAt = job?.NextRunAt;
        Status = phase switch
        {
            // "Syncs only when you press Sync now", while Sync now is offered; else the schedule's name alone
            SourcePhase.Manual => UploadsOn ? Loc.Get("Social_StatusManual") : SocialSourcesViewModel.ScheduleName(source.Schedule),
            SourcePhase.Scheduled when source.NextSyncAt is { } next => Loc.Format("Social_StatusScheduled", SocialSourcesViewModel.ScheduleName(source.Schedule), SocialSourcesViewModel.When(next)),
            SourcePhase.Scheduled => SocialSourcesViewModel.ScheduleName(source.Schedule),
            SourcePhase.Syncing => Loc.Get("Social_StatusSyncing"),
            SourcePhase.Retrying when retryAt is { } r => Loc.Format("Social_StatusRetrying", SocialSourcesViewModel.When(r)),
            SourcePhase.Retrying => Loc.Get("Social_StatusRetryingSoon"),
            SourcePhase.Failed => Loc.Get(UploadsOn ? "Social_StatusFailed" : "Social_StatusFailedQuiet"),
            SourcePhase.Cancelled when source.Schedule != SourceSchedules.Manual && source.NextSyncAt is { } next2 => Loc.Format("Social_StatusCancelledNext", SocialSourcesViewModel.When(next2)),
            SourcePhase.Cancelled => Loc.Get(UploadsOn ? "Social_StatusCancelled" : "Social_StatusCancelledQuiet"),
            SourcePhase.OffNotConnected => Loc.Get("Social_StatusOffNotConnected"),
            _ => Loc.Get("Social_StatusOff"),
        };
        Status = Loc.KeepLastWords(Status);
        StatusTip = phase == SourcePhase.Scheduled && source.NextSyncAt is { } n2 ? Loc.UtcText(n2) : phase == SourcePhase.Retrying && retryAt is { } r2 ? Loc.UtcText(r2) : null;
        // Why the last sync failed: the server's code in the admin's words when the app knows it, else its own words
        var error = phase is SourcePhase.Failed or SourcePhase.Retrying ? job?.LastError ?? source.LastError : null;
        Detail = error is null ? "" : ReasonText(error, uploadsOn: UploadsOn);
        HasDetail = Detail.Length > 0;
        LastSync = source.LastSyncAt is { } last ? Loc.Format("Social_LastSync", SocialSourcesViewModel.When(last)) : Loc.Get("Social_NeverSynced");
        LastSyncTip = source.LastSyncAt is { } l2 ? Loc.UtcText(l2) : null;
        var parts = new List<string>();
        foreach (var state in new[] { "posted", "pending", "awaiting_file", "skipped" })
        {
            var n = source.Count(state);
            if (n > 0) parts.Add(Loc.Format("Social_Count_" + state, n).Replace(' ', ' '));
        }
        Counts = Loc.Commas(parts);
        HasCounts = parts.Count > 0;
        UpdateJob(job);
    }

    /// <summary>The server's reason in the app's words when its code is known; the raw text on its own line otherwise.</summary>
    internal static string ReasonText(string error, bool labelled = true, bool uploadsOn = true)
    {
        var reason = SourceText.Explain(error);
        if (reason.Code is { } code)
        {
            var key = !uploadsOn && code == "upload_incomplete" ? "SocialReason_upload_incompleteQuiet" : "SocialReason_" + code;
            var text = Loc.Get(key);
            // The server's status, as a number in the admin's digits on its own line; its own words only for an unexpected failure
            if (reason.HttpStatus is { } status) text = Loc.WithDetail(text, Loc.Format("SocialReason_HttpStatus", status));
            return reason.Detail is { } d ? Loc.WithDetail(text, d) : text;
        }
        return labelled ? Loc.GetWithDetail("Social_Reason", reason.Detail) : reason.Detail ?? error;
    }

    private void UpdateJob(SyncJob? job)
    {
        HasJob = job is not null;
        CanCancelJob = job?.CanCancel ?? false;
        JobIsCancelled = job?.IsCancelled ?? false;
        // Retry runs a failed sync again or starts a new sync after Cancel: not offered while uploads are off
        _retryable = job?.CanRetry ?? false;
        CanRetryJob = _retryable && UploadsOn;
        if (job is null)
        {
            JobStatus = "";
            JobDetail = "";
            HasJobDetail = false;
            JobEntries.Clear();
            ShowJob = false;
            return;
        }
        var max = Math.Max(job.MaxAttempts, job.Attempts);
        JobStatus = Loc.KeepLastWords(job.Status switch
        {
            "queued" => Loc.Get("SocialJob_queued"),
            "running" => Loc.Format("SocialJob_running", job.Attempts, max),
            "retrying" when job.NextRunAt is { } at => Loc.Format("SocialJob_retrying", job.Attempts, max, SocialSourcesViewModel.When(at)),
            "retrying" => Loc.Format("SocialJob_retryingSoon", job.Attempts, max),
            "failed" => Loc.Format(UploadsOn ? "SocialJob_failed" : "SocialJob_failedQuiet", job.Attempts, max),
            "succeeded" when job.FinishedAt is { } done => Loc.Format("SocialJob_succeededAt", SocialSourcesViewModel.When(done)),
            // Without its "Retry starts a new sync" while that Retry isn't offered (uploads off)
            "cancelled" => UploadsOn ? CancelledLine(job) : job.CancelledBy is { Length: > 0 } by ? Loc.GetWithDetail("SocialJob_cancelledByQuiet", by) : Loc.Get("SocialJob_cancelledQuiet"),
            _ => Loc.Get("SocialJob_succeeded"),
        });
        JobStatusTip = job.Status == "retrying" && job.NextRunAt is { } t ? Loc.UtcText(t) : job.FinishedAt is { } f ? Loc.UtcText(f) : null;
        JobDetail = job.LastError is { } e && job.Status is "failed" or "retrying" ? ReasonText(e, uploadsOn: UploadsOn) : "";
        HasJobDetail = JobDetail.Length > 0;
        JobEntries.Clear();
        foreach (var line in job.Log ?? Array.Empty<SyncJobLogLine>())
        {
            JobEntries.Add(new LogEntry(Loc.TimeWithZone(line.At.LocalDateTime), Loc.UtcText(line.At), BulkImportViewModel.LogText(line, UploadsOn).Split('\n'), line.Event is "failed" or "failed_attempt"));
        }
    }

    internal void SetItems(IReadOnlyList<ImportSourceItem> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(new SourceItemRow(item));
        ItemsMessage = items.Count == 0 ? Loc.Get("Social_ItemsEmpty") : "";
    }
}

/// <summary>An item of a source: what it is, what became of it and why, and where it was first posted.</summary>
public sealed class SourceItemRow
{
    public SourceItemRow(ImportSourceItem item)
    {
        var kind = item.Kind is { } k ? Loc.Get("SocialKind_" + k) : Loc.Get("SocialKind_unknown");
        var state = Loc.Get("SocialItemState_" + item.State);
        Text = Loc.Commas(new[] { kind, state });
        Note = item.Note is { } note ? Loc.KeepLastWords(Loc.Get("SocialItemNote_" + note)) : "";
        HasNote = Note.Length > 0;
        IsProblem = item.State is "skipped" or "awaiting_file";
        Original = item.OriginalUrl ?? "";
        HasOriginal = Original.Length > 0;
        When = item.OriginalAt is { } at ? Loc.Format("Social_ItemFirstPosted", SocialSourcesViewModel.When(at)) : "";
        HasWhen = When.Length > 0;
    }

    public string Text { get; }
    public string Note { get; }
    public bool HasNote { get; }
    public bool IsProblem { get; }
    public string Original { get; }
    public bool HasOriginal { get; }
    public Microsoft.UI.Xaml.TextAlignment OriginalAlignment => Loc.IsArabic ? Microsoft.UI.Xaml.TextAlignment.Right : Microsoft.UI.Xaml.TextAlignment.Left;
    public string When { get; }
    public bool HasWhen { get; }
}

/// <summary>A Takeout file: its name, what became of it and why, the record it matched, or the records an admin picks from.</summary>
public sealed partial class TakeoutFileRow : ObservableObject
{
    public TakeoutFileRow(TakeoutFile file, bool uploadsOn = true)
    {
        File = file;
        Id = file.Id;
        Name = file.Name;
        // A title that may be Arabic: the extension (Latin) would split its line, so the name is shown without it
        DisplayName = System.IO.Path.GetFileNameWithoutExtension(file.Name);
        IsProblem = file.State is TakeoutStates.Unmatched or TakeoutStates.Failed;
        IsAmbiguous = file.State == TakeoutStates.Ambiguous;
        IsDone = file.State == TakeoutStates.Attached;
        // The record's title is the owner's own words (often Latin in an Arabic page): on its own line
        Match = file.Item is { } item ? item.Title ?? item.ItemId : "";
        HasMatch = Match.Length > 0;
        foreach (var c in file.Candidates ?? Array.Empty<TakeoutCandidate>()) Candidates.Add(new TakeoutCandidateRow(c, this));
        CanUpload = file.CanUpload;
        SetUploadsOn(uploadsOn);
    }

    internal void SetUploadsOn(bool uploadsOn)
    {
        var stateKey = !uploadsOn && File.State == TakeoutStates.Matched ? "TakeoutState_matchedQuiet" : "TakeoutState_" + File.State;
        var state = Loc.Get(stateKey);
        StateText = File.MatchedBy is { } by && File.State is TakeoutStates.Matched or TakeoutStates.Uploading or TakeoutStates.Uploaded or TakeoutStates.Attached
            ? Loc.Commas(new[] { state, Loc.Get("TakeoutMatchedBy_" + by) })
            : state;
        var noteKey = File.Note is { } note
            ? (!uploadsOn && note == "upload_incomplete" ? "TakeoutNote_upload_incompleteQuiet" : "TakeoutNote_" + note)
            : null;
        Note = noteKey is not null ? Loc.KeepLastWords(Loc.Get(noteKey)) : "";
        HasNote = Note.Length > 0;
    }

    public TakeoutFile File { get; }
    public string Id { get; }
    public string Name { get; }
    public string DisplayName { get; }
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private bool _hasNote;
    public bool IsProblem { get; }
    public bool IsAmbiguous { get; }
    public bool IsDone { get; }
    public string Match { get; }
    public bool HasMatch { get; }
    public bool CanUpload { get; }
    public ObservableCollection<TakeoutCandidateRow> Candidates { get; } = new();

    [ObservableProperty] private string _progress = "";
    [ObservableProperty] private bool _hasProgress;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _hasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    partial void OnProgressChanged(string value) => HasProgress = value.Length > 0;
    partial void OnErrorChanged(string value) => HasError = value.Length > 0;
}

/// <summary>A record an ambiguous file could be: its title, when it was first posted, and its link.</summary>
public sealed class TakeoutCandidateRow
{
    public TakeoutCandidateRow(TakeoutCandidate candidate, TakeoutFileRow file)
    {
        ItemId = candidate.ItemId;
        File = file;
        Title = candidate.Title ?? candidate.ItemId;
        When = candidate.OriginalAt is { } at ? Loc.Format("Social_ItemFirstPosted", SocialSourcesViewModel.When(at)) : "";
        HasWhen = When.Length > 0;
        Original = candidate.OriginalUrl ?? "";
        HasOriginal = Original.Length > 0;
    }

    public string ItemId { get; }
    public TakeoutFileRow File { get; }
    public string Title { get; }
    public string When { get; }
    public bool HasWhen { get; }
    public string Original { get; }
    public bool HasOriginal { get; }
    public Microsoft.UI.Xaml.TextAlignment OriginalAlignment => Loc.IsArabic ? Microsoft.UI.Xaml.TextAlignment.Right : Microsoft.UI.Xaml.TextAlignment.Left;
}

/// <summary>An account the owner connected, offered as a source.</summary>
public sealed partial class ConnectionRow : ObservableObject
{
    public ConnectionRow(SourceConnection connection)
    {
        Connection = connection;
        Label = connection.Label;
        NetworkLine = Loc.WithDetail(SocialSourcesViewModel.NetworkName(connection.Network), connection.Address);
    }

    public SourceConnection Connection { get; }
    public string Label { get; }
    public string NetworkLine { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private bool _isAdded;

    public bool CanAdd => !IsAdded;
}
