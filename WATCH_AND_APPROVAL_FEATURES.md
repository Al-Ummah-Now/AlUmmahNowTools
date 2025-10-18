# Watch Management and Approval Workflow Features

## Table of Contents
1. [Overview](#overview)
2. [Architecture Overview](#architecture-overview)
3. [Watch Management Feature](#watch-management-feature)
4. [Approval Workflow Feature](#approval-workflow-feature)
5. [End-to-End Workflow](#end-to-end-workflow)
6. [Configuration](#configuration)
7. [API Reference](#api-reference)
8. [Troubleshooting](#troubleshooting)

---

## Overview

### Feature Summary

The **Watch Management** and **Approval Workflow** features provide a comprehensive, real-time solution for monitoring video file directories and managing approval workflows in the AlUmmahNow Visual Importer application. These features enable automatic detection of new video files, user-driven approval processes, and seamless integration with the existing import pipeline.

#### Key Capabilities

- **Real-time File Monitoring**: Automatically detects new video files in configured directories
- **Channel-Based Organization**: Associates watched directories with channel names for logical grouping
- **User Approval Workflow**: Requires explicit user approval before files enter the import pipeline
- **Bulk Operations**: Supports bulk approval/rejection with advanced filtering
- **SignalR Integration**: Provides real-time updates across all connected clients
- **Multi-Format Support**: Monitors multiple video formats (.mp4, .mkv, .avi, .mov, etc.)
- **Statistics Dashboard**: Real-time metrics on watches, approvals, and processing status

### Business Value

1. **Quality Control**: Ensures only approved content enters the import pipeline
2. **Automation**: Reduces manual effort by automatically detecting new files
3. **Visibility**: Provides real-time insights into pending and processed files
4. **Flexibility**: Supports multiple channels with independent watch configurations
5. **Scalability**: Handles large volumes of files across multiple directories
6. **Audit Trail**: Tracks who approved/rejected files and when

### Technology Stack

- **Frontend**: Uno Platform (cross-platform XAML), MVVM pattern with CommunityToolkit.Mvvm
- **Backend**: ASP.NET Core, SignalR for real-time communication
- **Database**: Entity Framework Core with SQL Server
- **Architecture**: Event-driven with SignalR Hub as message broker

---

## Architecture Overview

### System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     AlUmmahNow Visual Importer                  │
│                        (Uno Platform Client)                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌──────────────────────┐      ┌──────────────────────┐       │
│  │  WatchViewModel      │      │  ApprovalViewModel   │       │
│  │  - Manages watches   │      │  - Handles approvals │       │
│  │  - Real-time updates │      │  - Filtering/sorting │       │
│  │  - UI state          │      │  - Bulk operations   │       │
│  └──────────┬───────────┘      └──────────┬───────────┘       │
│             │                              │                   │
│             └──────────────┬───────────────┘                   │
│                            │                                   │
│                  ┌─────────▼────────────┐                      │
│                  │ FileScanSignalRClient│                      │
│                  │ - Connection mgmt    │                      │
│                  │ - Event handling     │                      │
│                  │ - API calls          │                      │
│                  └─────────┬────────────┘                      │
└────────────────────────────┼─────────────────────────────────┘
                             │
                    SignalR WebSocket
                             │
┌────────────────────────────▼─────────────────────────────────┐
│                  AlUmmahNow.FileScanService                   │
│                    (ASP.NET Core Backend)                     │
├──────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              FileScanHub (SignalR Hub)               │   │
│  │  - Connection management                             │   │
│  │  - Group management (watches, approvals)             │   │
│  │  - Client API methods                                │   │
│  │  - Event broadcasting                                │   │
│  └───────────────────┬──────────────────────────────────┘   │
│                      │                                       │
│         ┌────────────┴──────────┬──────────────────┐        │
│         │                       │                  │        │
│  ┌──────▼────────┐    ┌─────────▼────────┐  ┌─────▼─────┐ │
│  │ FileWatchSvc  │    │ ApprovalService  │  │  DbContext │ │
│  │ - FileWatcher │    │ - Decision logic │  │  - EF Core │ │
│  │ - File detect │    │ - Bulk ops       │  │  - SQL Srv │ │
│  │ - Events      │    │ - Statistics     │  └────────────┘ │
│  └───────────────┘    └──────────────────┘                 │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### Data Flow

1. **Watch Creation**: User creates watch → SignalR client → FileScanHub → FileWatchService → Database
2. **File Detection**: FileSystemWatcher → FileWatchService → FileScanHub → SignalR broadcast → All clients
3. **Approval Decision**: User decides → SignalR client → FileScanHub → ApprovalService → Database → Broadcast
4. **Real-time Updates**: Service event → FileScanHub → SignalR group → Subscribed clients

### Component Relationships

```
WatchViewModel ──────────────┐
                            │
ApprovalViewModel ──────────┼──> FileScanSignalRClient
                            │        │
WatchManagementPage ────────┘        │
                                     │
ApprovalWorkflowPage                 │ SignalR Connection
                                     │
                                     ▼
                              FileScanHub ──┬──> FileWatchService ──> FileSystemWatcher
                                           │
                                           └──> ApprovalService ──> Database (EF Core)
```

---

## Watch Management Feature

### What It Does

The Watch Management feature monitors file system directories for new video files and automatically adds them to the approval queue. It provides:

- **Directory Monitoring**: Uses .NET FileSystemWatcher for real-time file detection
- **Configurable Delays**: Processing delay to avoid detecting files mid-copy
- **Extension Filtering**: Monitors only specified video file extensions
- **Subdirectory Support**: Optional recursive directory monitoring
- **Multi-Channel Support**: Independent watches for different channels
- **Active/Inactive States**: Watches can be paused and resumed

### User Guide

#### Creating a New Watch

1. Navigate to the **Watch Management** page
2. Click **Create New Watch** button
3. Fill in the watch configuration:
   - **Channel Name**: Logical name for the content source (e.g., "Islamic Lectures")
   - **Directory Path**: Absolute path to monitor (e.g., "C:\Videos\Import")
   - **Processing Delay**: Seconds to wait before processing detected files (default: 5s)
   - **Include Subdirectories**: Whether to monitor child directories
   - **File Extensions**: Comma-separated list of extensions (default: .mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm)
4. Click **Create Watch**

**Validation Rules**:
- Channel name: 2-100 characters, must be unique
- Directory path: Must exist on disk, cannot be duplicate
- Processing delay: 1-300 seconds
- File extensions: At least one extension required

#### Managing Existing Watches

**View Watch Status**:
- Active/inactive indicator (green/red dot)
- Total files detected
- Pending approvals count
- Last file detected timestamp

**Stop a Watch**:
1. Select the watch from the list
2. Click **Stop** button
3. Watch becomes inactive but remains in the database

**Delete a Watch**:
1. Select the watch
2. Click **Delete** button
3. Watch and associated pending approvals are removed

**View Watch Details**:
1. Select the watch
2. Click **View Details**
3. Filters approval list to show only this channel's files

### Technical Architecture

#### ViewModel: WatchViewModel

**File**: `/home/meywd/AlUmmahNowTools/AlUmmahNow.VisualImporterUno/AlUmmahNow.VisualImporterUno/ViewModels/WatchViewModel.cs`

**Key Responsibilities**:
- Manages watch lifecycle (create, stop, delete)
- Subscribes to SignalR events for real-time updates
- Provides statistics aggregation
- Handles UI thread synchronization via DispatcherQueue
- Validates user input before submission

**Observable Properties**:
```csharp
// Status and Loading
bool IsLoading
string StatusMessage
WatchStatusDto? SelectedWatch

// Statistics
int TotalWatches
int ActiveWatchesCount
int TotalFilesDetected
int PendingApprovalsCount
int ApprovedCount
int RejectedCount

// Collections
ObservableCollection<WatchStatusDto> ActiveWatches
ObservableCollection<PendingApprovalDto> PendingApprovals
ObservableCollection<PendingApprovalDto> FilteredPendingApprovals
```

**Key Commands**:
```csharp
IRelayCommand OpenCreateWatchDialogCommand
IRelayCommand CreateWatchCommand           // CanExecute: IsChannelNameValid && IsWatchPathValid
IRelayCommand StopWatchCommand             // CanExecute: HasSelectedWatch
IRelayCommand DeleteWatchCommand           // CanExecute: HasSelectedWatch
IRelayCommand RefreshWatchesCommand
IRelayCommand ViewWatchDetailsCommand      // CanExecute: HasSelectedWatch
```

**SignalR Event Subscriptions**:
```csharp
// Connection events
ServiceStatusChanged    // Triggers auto-refresh when connected
ErrorOccurred          // Displays errors in status message

// Watch events
FileDetected           // Updates watch file count, refreshes approvals
WatchStatusChanged     // Updates watch active state
WatchStopped           // Marks watch as inactive
WatchUpdated           // Triggers refresh
WatchDeleted           // Removes watch from collection

// Approval events
NewFileForApproval         // Refreshes approval list
ApprovalStatusChanged      // Updates approval status in collection
ApprovalDecisionProcessed  // Triggers approval refresh
BulkApprovalProcessed      // Triggers approval refresh
```

**Thread Safety**:
The ViewModel uses `DispatcherQueue` to ensure all UI updates occur on the UI thread:
```csharp
private void DispatcherExecute(Action action)
{
    if (_dispatcherQueue?.HasThreadAccess == true)
        action();
    else if (_dispatcherQueue != null)
        _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => action());
    else
        action(); // Fallback
}
```

#### UI: WatchManagementPage.xaml

**File**: `/home/meywd/AlUmmahNowTools/AlUmmahNow.VisualImporterUno/AlUmmahNow.VisualImporterUno/Views/WatchManagementPage.xaml`

**Layout Structure**:
1. **Header Section**: Page title, status message, action buttons
2. **Statistics Cards**: 4-column grid showing key metrics
3. **Active Watches List**: ListView with watch details and inline actions
4. **Pending Approvals List**: Integrated approval queue view
5. **Create Watch Dialog**: Modal dialog for watch creation
6. **Loading Overlay**: Full-screen loading indicator

**Design Pattern**: Card-based UI with ThemeShadow for depth, color-coded status indicators

**Key UI Elements**:
```xml
<!-- Statistics Dashboard -->
<Grid> <!-- 4 columns -->
  <Border Style="StatCardStyle"> <!-- Total Watches -->
  <Border Style="StatCardStyle"> <!-- Active Watches -->
  <Border Style="StatCardStyle"> <!-- Files Detected -->
  <Border Style="StatCardStyle"> <!-- Pending Approvals -->
</Grid>

<!-- Active Watches List -->
<ListView ItemsSource="{x:Bind ViewModel.ActiveWatches}">
  <DataTemplate x:DataType="WatchStatusDto">
    <!-- Status indicator (colored ellipse) -->
    <!-- Channel name, path, status -->
    <!-- Action buttons (View Details, Stop) -->
  </DataTemplate>
</ListView>
```

#### SignalR Integration

**Connection Lifecycle**:
1. ViewModel constructor: Subscribe to events
2. InitializeAsync: Connect to SignalR hub (if not connected)
3. OnConnectedAsync: Auto-refresh watches
4. OnDisconnectedAsync: Clear collections, show disconnected message
5. Dispose: Unsubscribe from all events

**Event Handling Pattern**:
```csharp
private void OnFileDetected(object? sender, FileDetectedEventArgs e)
{
    DispatcherExecute(() =>
    {
        StatusMessage = $"File detected: {e.FileName} in {e.ChannelName}";

        // Update local state
        var watch = ActiveWatches.FirstOrDefault(w => w.WatchId == e.WatchId);
        if (watch != null)
        {
            watch.TotalFilesDetected++;
            UpdateStatistics();
        }

        // Refresh approvals asynchronously
        _ = Task.Run(async () => await RefreshPendingApprovalsAsync());
    });
}
```

### Backend: FileWatchService

**Service Interface**: `IFileWatchService` (file: `/home/meywd/AlUmmahNowTools/AlUmmahNow.FileScanService/Services/IWatchServices.cs`)

**Core Methods**:
```csharp
Task<Guid> StartWatchAsync(CreateWatchRequest request)
Task<bool> StopWatchAsync(Guid watchId)
Task<List<WatchStatusDto>> GetActiveWatchesAsync()
Task<WatchStatusDto?> GetWatchStatusAsync(Guid watchId)
Task<bool> UpdateWatchAsync(Guid watchId, CreateWatchRequest request)
Task<bool> DeleteWatchAsync(Guid watchId)
```

**Events Raised**:
```csharp
event EventHandler<FileDetectedEventArgs> FileDetected;
event EventHandler<WatchStatusChangedEventArgs> WatchStatusChanged;
```

**File Detection Logic**:
1. FileSystemWatcher.Created event fires
2. Delay for ProcessingDelayMs (avoid incomplete file copies)
3. Verify file still exists and is readable
4. Check file extension against configured list
5. Calculate file size and hash
6. Create PendingApproval record in database
7. Raise FileDetected event → FileScanHub broadcasts to clients

---

## Approval Workflow Feature

### What It Does

The Approval Workflow feature provides a comprehensive interface for reviewing and approving detected files before they enter the import pipeline. It includes:

- **Centralized Approval Queue**: All detected files across all channels
- **Advanced Filtering**: Channel, priority, size, date, extension filters
- **Sorting Options**: By date, filename, size, priority, channel
- **Bulk Operations**: Approve/reject all files matching current filters
- **Selection Management**: Multi-select for batch approval
- **Real-time Updates**: Live statistics and approval status changes
- **Activity Log**: Audit trail of all approval actions
- **File Preview**: (Planned) Preview file details before approval

### User Guide

#### Viewing Pending Approvals

1. Navigate to the **Approval Workflow** page
2. View statistics dashboard:
   - **Pending**: Files awaiting decision
   - **Approved**: Files approved for import
   - **Rejected**: Files rejected
   - **Total Size**: Combined size of all files
3. Browse the approvals list with file details:
   - Filename, channel, size, extension
   - Detection timestamp
   - Priority indicator
   - File exists status

#### Filtering Approvals

**Available Filters**:
- **Channel**: Select specific channel or "All Channels"
- **Priority**: Filter by priority level (1=highest to 5=lowest)
- **File Size**: Min/max size in bytes
- **Date Range**: From/To date filters
- **Extension**: Comma-separated list (e.g., "mp4,mkv")

**Show Only Pending**: Toggle to show/hide already processed files

**Sorting**:
- Sort by: Date Detected (default), File Name, File Size, Priority, Channel
- Toggle ascending/descending order

#### Approving/Rejecting Files

**Single File**:
1. Click **Approve** or **Reject** button on file card
2. File status updates immediately
3. Activity log records the action

**Multiple Files (Selection)**:
1. Check checkboxes for desired files
2. Click **Approve Selected** (green button in header)
3. Or click **Reject Selected** (red button in header)
4. Confirmation and activity log update

**Bulk Operations (Filter-based)**:
1. Set desired filters (channel, size, date, etc.)
2. Click **Bulk Approve All** or **Bulk Reject All** in filters panel
3. All files matching current filters are processed
4. Status message shows count processed

#### Activity Log

The activity log (right panel) shows real-time updates:
- File approvals/rejections
- Bulk operation results
- Errors and warnings
- System messages

Icons indicate message type:
- ✓ Success (green)
- ⚠ Warning (yellow)
- ✗ Error (red)
- ℹ Information (blue)

### Technical Architecture

#### ViewModel: ApprovalViewModel

**File**: `/home/meywd/AlUmmahNowTools/AlUmmahNow.VisualImporterUno/AlUmmahNow.VisualImporterUno/ViewModels/ApprovalViewModel.cs`

**Key Responsibilities**:
- Manages approval lifecycle
- Implements filtering and sorting logic
- Handles bulk operations
- Maintains activity log
- Tracks selection state for batch operations

**Observable Properties**:
```csharp
// Status
bool IsLoading
string StatusMessage
bool IsProcessingApproval
bool IsProcessingBulkApproval

// Current State
PendingApprovalDto? CurrentApproval
ApprovalStatistics Statistics

// Filters
string? ChannelFilter
int? PriorityFilter
long? MinSizeFilter
long? MaxSizeFilter
DateTime? DateFromFilter
DateTime? DateToFilter
string? ExtensionFilter
bool ShowOnlyPending

// Sorting
string SortBy              // "DetectedAt", "FileName", "FileSize", "Priority", "ChannelName"
bool SortDescending

// Selection
bool HasSelection
int SelectedCount

// Collections
ObservableCollection<PendingApprovalDto> PendingApprovals
ObservableCollection<PendingApprovalDto> FilteredApprovals
ObservableCollection<PendingApprovalDto> SelectedApprovals
ObservableCollection<string> AvailableChannels
ObservableCollection<ApprovalLogMessage> ActivityLog
```

**Key Commands**:
```csharp
IRelayCommand RefreshApprovalsCommand        // CanExecute: !IsLoading && IsConnected
IRelayCommand ApproveSelectedCommand         // CanExecute: HasSelection && !IsProcessing
IRelayCommand RejectSelectedCommand          // CanExecute: HasSelection && !IsProcessing
IRelayCommand BulkApproveCommand             // CanExecute: !IsProcessing && HasActiveFilters
IRelayCommand BulkRejectCommand              // CanExecute: !IsProcessing && HasActiveFilters
IRelayCommand PreviewFileCommand
IRelayCommand SelectAllCommand
IRelayCommand DeselectAllCommand
IRelayCommand ToggleSelectionCommand
IRelayCommand ClearFiltersCommand
IRelayCommand ClearActivityLogCommand
IRelayCommand ApprovalsByChannelCommand
```

**Filtering Logic**:
```csharp
private void ApplyFiltersAndSorting()
{
    var filtered = PendingApprovals.AsEnumerable();

    // Apply ShowOnlyPending filter
    if (ShowOnlyPending)
        filtered = filtered.Where(a => a.Status == ApprovalStatus.Pending);

    // Apply channel filter
    if (!string.IsNullOrWhiteSpace(ChannelFilter) && ChannelFilter != "All Channels")
        filtered = filtered.Where(a => a.ChannelName.Equals(ChannelFilter, OrdinalIgnoreCase));

    // Apply priority filter
    if (PriorityFilter.HasValue)
        filtered = filtered.Where(a => a.Priority == PriorityFilter.Value);

    // Apply size filters
    if (MinSizeFilter.HasValue)
        filtered = filtered.Where(a => a.FileSize >= MinSizeFilter.Value);
    if (MaxSizeFilter.HasValue)
        filtered = filtered.Where(a => a.FileSize <= MaxSizeFilter.Value);

    // Apply date filters
    if (DateFromFilter.HasValue)
        filtered = filtered.Where(a => a.DetectedAt >= DateFromFilter.Value);
    if (DateToFilter.HasValue)
        filtered = filtered.Where(a => a.DetectedAt <= DateToFilter.Value);

    // Apply extension filter
    if (!string.IsNullOrWhiteSpace(ExtensionFilter))
    {
        var extensions = ExtensionFilter.Split(',').Select(e => e.Trim().ToLower());
        filtered = filtered.Where(a => extensions.Contains(a.Extension.ToLower()));
    }

    // Apply sorting
    filtered = SortBy switch
    {
        "FileName" => SortDescending
            ? filtered.OrderByDescending(a => a.FileName)
            : filtered.OrderBy(a => a.FileName),
        "FileSize" => SortDescending
            ? filtered.OrderByDescending(a => a.FileSize)
            : filtered.OrderBy(a => a.FileSize),
        "Priority" => SortDescending
            ? filtered.OrderByDescending(a => a.Priority)
            : filtered.OrderBy(a => a.Priority),
        "ChannelName" => SortDescending
            ? filtered.OrderByDescending(a => a.ChannelName)
            : filtered.OrderBy(a => a.ChannelName),
        _ => SortDescending
            ? filtered.OrderByDescending(a => a.DetectedAt)
            : filtered.OrderBy(a => a.DetectedAt)
    };

    FilteredApprovals.Clear();
    foreach (var approval in filtered)
        FilteredApprovals.Add(approval);
}
```

**Bulk Operations**:
```csharp
private BulkApprovalRequest BuildBulkApprovalRequest(ApprovalStatus decision)
{
    var request = new BulkApprovalRequest
    {
        Decision = decision,
        ApprovedBy = Environment.UserName,
        Notes = $"Bulk {decision} via approval workflow"
    };

    // Copy current filters to bulk request
    if (!string.IsNullOrWhiteSpace(ChannelFilter) && ChannelFilter != "All Channels")
        request.ChannelName = ChannelFilter;
    if (MinSizeFilter.HasValue)
        request.MinFileSize = MinSizeFilter.Value;
    if (MaxSizeFilter.HasValue)
        request.MaxFileSize = MaxSizeFilter.Value;
    if (DateFromFilter.HasValue)
        request.DetectedAfter = DateFromFilter.Value;
    if (DateToFilter.HasValue)
        request.DetectedBefore = DateToFilter.Value;
    if (!string.IsNullOrWhiteSpace(ExtensionFilter))
        request.FileExtensions = ExtensionFilter.Split(',').Select(e => e.Trim()).ToList();

    return request;
}
```

**Activity Log Management**:
```csharp
private void AddLog(string message, LogType type)
{
    var logMessage = new ApprovalLogMessage
    {
        Message = message,
        Timestamp = DateTime.Now,
        Type = type
    };

    ActivityLog.Insert(0, logMessage); // Newest first

    // Keep only last 200 messages
    while (ActivityLog.Count > 200)
        ActivityLog.RemoveAt(ActivityLog.Count - 1);
}
```

#### UI: ApprovalWorkflowPage.xaml

**File**: `/home/meywd/AlUmmahNowTools/AlUmmahNow.VisualImporterUno/AlUmmahNow.VisualImporterUno/Views/ApprovalWorkflowPage.xaml`

**Layout Structure**:
```
Header Section (Grid.Row=0)
├── Title and Status
├── Bulk Action Buttons (Refresh, Select All, Clear, Approve, Reject)
└── Statistics Dashboard (4 cards: Pending, Approved, Rejected, Total Size)

Main Content (Grid.Row=1) - 3 Columns
├── Left Panel (300px): Filters
│   ├── Channel Filter (ComboBox)
│   ├── Priority Filter (ComboBox)
│   ├── File Size Filter (Min/Max TextBoxes)
│   ├── Date Range Filter (DatePickers)
│   ├── Extension Filter (TextBox)
│   └── Bulk Actions (Approve All / Reject All buttons)
│
├── Center Panel (Flex): Approvals List
│   ├── List Header (count, sort options)
│   ├── Progress Bar (when loading)
│   ├── ListView (approval items)
│   └── Empty State (when no results)
│
└── Right Panel (320px): Preview and Activity
    ├── File Preview Card (thumbnail, details, actions)
    └── Activity Log (scrollable list)
```

**Key Data Templates**:

**Approval Item Template**:
```xml
<DataTemplate x:DataType="models:PendingApprovalDto">
  <Border> <!-- Card styling -->
    <Grid>
      <CheckBox /> <!-- Selection -->
      <Grid> <!-- File Information -->
        <TextBlock Text="{x:Bind FileName}"/>
        <Border> <!-- Extension badge -->
        <Border> <!-- Priority badge -->
        <StackPanel> <!-- Metadata: Channel, Size, Date -->
      </Grid>
      <StackPanel> <!-- Actions: Preview, Approve, Reject -->
    </Grid>
  </Border>
</DataTemplate>
```

**Activity Log Template**:
```xml
<DataTemplate x:DataType="vm:ApprovalLogMessage">
  <Border>
    <Grid>
      <TextBlock Text="{x:Bind TypeIcon}"/> <!-- ✓ ⚠ ✗ ℹ -->
      <TextBlock Text="{x:Bind DisplayText}"/> <!-- [HH:mm:ss] Message -->
    </Grid>
  </Border>
</DataTemplate>
```

**Design Features**:
- Color-coded statistics cards (yellow=pending, green=approved, red=rejected, blue=size)
- Priority badges with dynamic colors
- Extension badges
- Empty state illustrations
- Loading indicators
- Responsive layout

### Backend: ApprovalService

**Service Interface**: `IApprovalService` (file: `/home/meywd/AlUmmahNowTools/AlUmmahNow.FileScanService/Services/IWatchServices.cs`)

**Core Methods**:
```csharp
Task<List<PendingApprovalDto>> GetPendingApprovalsAsync()
Task<List<PendingApprovalDto>> GetPendingApprovalsByChannelAsync(string channelName)
Task<PendingApprovalDto?> GetPendingApprovalAsync(Guid approvalId)
Task<bool> ProcessApprovalDecisionAsync(ApprovalDecisionRequest request)
Task<int> ProcessBulkApprovalAsync(BulkApprovalRequest request)
Task<Guid> AddPendingApprovalAsync(Guid channelWatchId, string filePath)
Task<int> CleanupExpiredApprovalsAsync()
Task<ApprovalStatistics> GetApprovalStatisticsAsync(Guid? channelWatchId = null)
```

**Events Raised**:
```csharp
event EventHandler<ApprovalStatusChangedEventArgs> ApprovalStatusChanged;
```

**Approval Decision Logic**:
1. Receive `ApprovalDecisionRequest` with list of approval IDs and decision
2. Load all PendingApproval records from database
3. Update Status, ApprovedAt, ApprovedBy, ApprovalNotes
4. Save changes to database
5. Raise ApprovalStatusChanged event for each → FileScanHub broadcasts
6. Return success/failure

**Bulk Approval Logic**:
1. Receive `BulkApprovalRequest` with filters and decision
2. Query PendingApproval table with filters:
   - Channel name (exact match)
   - File size range (min/max)
   - Detection date range (after/before)
   - File extensions (in list)
3. Update all matching records with decision
4. Save changes in batch
5. Broadcast bulk update event
6. Return count of processed records

---

## End-to-End Workflow

### Complete Lifecycle: From Detection to Import

```
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 1: Watch Configuration                                     │
└──────────────────────────────────────────────────────────────────┘
    User creates watch for channel "Islamic Lectures"
    Path: C:\Videos\Lectures
    Extensions: .mp4,.mkv
    Processing Delay: 5 seconds
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 2: File Detection                                          │
└──────────────────────────────────────────────────────────────────┘
    New file appears: "lecture_001.mp4" (1.2 GB)
    FileSystemWatcher.Created event fires
    Service waits 5 seconds (processing delay)
    Service verifies file exists and is accessible
    Service calculates file size and metadata
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 3: Pending Approval Creation                               │
└──────────────────────────────────────────────────────────────────┘
    ApprovalService.AddPendingApprovalAsync() called
    PendingApproval record created in database:
      - Status: Pending
      - Priority: 3 (default)
      - DetectedAt: Current timestamp
    FileDetected event raised
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 4: SignalR Broadcast                                       │
└──────────────────────────────────────────────────────────────────┘
    FileScanHub receives FileDetected event
    Hub broadcasts to SignalR groups:
      - "watch-{watchId}" group → WatchViewModel updates
      - "approvals" group → ApprovalViewModel updates
    All connected clients receive real-time notification
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 5: Client UI Updates                                       │
└──────────────────────────────────────────────────────────────────┘
    WatchViewModel.OnFileDetected() handler:
      - Updates watch file count
      - Refreshes pending approvals list
      - Updates statistics dashboard
    ApprovalViewModel.OnNewFileForApproval() handler:
      - Adds file to approval queue
      - Updates statistics
      - Logs activity message
    UI automatically reflects new file in both pages
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 6: User Review                                             │
└──────────────────────────────────────────────────────────────────┘
    User navigates to Approval Workflow page
    Filters by channel "Islamic Lectures"
    Reviews file details:
      - Filename: lecture_001.mp4
      - Size: 1.2 GB
      - Detected: 2 minutes ago
    Previews thumbnail (if available)
    Decides to approve
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 7: Approval Decision                                       │
└──────────────────────────────────────────────────────────────────┘
    User clicks "Approve" button
    ApprovalViewModel.ApproveSelectedCommand executes
    SignalR client sends ProcessApprovalDecision request:
      - ApprovalIds: [guid]
      - Decision: Approved
      - ApprovedBy: Current user
    FileScanHub receives request
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 8: Approval Processing                                     │
└──────────────────────────────────────────────────────────────────┘
    ApprovalService.ProcessApprovalDecisionAsync() executes
    Database update:
      - Status: Approved
      - ApprovedAt: Current timestamp
      - ApprovedBy: Username
    ApprovalStatusChanged event raised
    FileScanHub broadcasts to "approvals" group
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 9: Post-Approval Updates                                   │
└──────────────────────────────────────────────────────────────────┘
    All clients receive ApprovalStatusChanged event
    UI updates:
      - File removed from pending list (if ShowOnlyPending=true)
      - Statistics updated (pending -1, approved +1)
      - Activity log: "Approved 1 file"
    Watch statistics updated
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│ STAGE 10: Ready for Import                                       │
└──────────────────────────────────────────────────────────────────┘
    File now has Status = Approved
    Import service can query approved files
    File enters standard import pipeline
    (Integration with existing import process)
```

### Example Scenario: Bulk Approval

**Scenario**: User wants to approve all MP4 files over 500MB detected in the last week for channel "Islamic Lectures"

**Steps**:
1. User navigates to Approval Workflow page
2. Sets filters:
   - Channel: "Islamic Lectures"
   - Extension: "mp4"
   - Min Size: 524288000 (500 MB in bytes)
   - Date From: (7 days ago)
3. Reviews filtered list (e.g., 15 files match)
4. Clicks **Bulk Approve All** button in filters panel
5. Backend builds BulkApprovalRequest with filter criteria
6. ApprovalService queries database with filters
7. Updates all 15 matching records to Status=Approved
8. Broadcasts BulkApprovalProcessed event
9. UI shows: "Bulk approved 15 files matching filters"
10. Activity log records the bulk operation
11. All 15 files now ready for import

### State Diagram: Approval Status Lifecycle

```
                     ┌──────────────┐
                     │  File        │
                     │  Detected    │
                     └──────┬───────┘
                            │
                            ▼
                     ┌──────────────┐
              ┌──────┤   Pending    ├──────┐
              │      └──────────────┘      │
              │                            │
        User Approves                 User Rejects
              │                            │
              ▼                            ▼
       ┌──────────────┐            ┌──────────────┐
       │   Approved   │            │   Rejected   │
       └──────┬───────┘            └──────────────┘
              │                           (Terminal State)
              │
       Import Starts
              │
              ▼
       ┌──────────────┐
       │ ImportStarted│
       └──────┬───────┘
              │
         ┌────┴────┐
         │         │
      Success    Failure
         │         │
         ▼         ▼
  ┌──────────┐  ┌──────────┐
  │ Import   │  │ Import   │
  │Completed │  │ Failed   │
  └──────────┘  └──────────┘
  (Terminal)    (Terminal)


  Alternate Paths:

  Pending ──[Auto-approve rules]──> AutoApproved ──> ImportStarted

  Pending ──[Expiry timeout]──> Expired (Terminal)
```

---

## Configuration

### SignalR Connection Setup

**User Secrets (Development)**:

File: `secrets.json` (managed via .NET User Secrets)

```json
{
  "FileScanService": {
    "BaseUrl": "https://localhost:7001",
    "HubPath": "/hubs/filescan"
  }
}
```

**Application Configuration**:

File: `appsettings.json`

```json
{
  "FileScanService": {
    "BaseUrl": "https://localhost:7001",
    "HubPath": "/hubs/filescan",
    "ReconnectDelaySeconds": 5,
    "MaxReconnectAttempts": 10,
    "ConnectionTimeoutSeconds": 30
  },
  "FileWatch": {
    "DefaultProcessingDelayMs": 5000,
    "DefaultFileExtensions": ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm",
    "MaxActiveWatches": 50,
    "EnableAutoApproval": false
  },
  "Approval": {
    "DefaultPriority": 3,
    "ExpirationDays": 30,
    "CleanupIntervalHours": 24
  }
}
```

### Service Requirements

**Backend Service (AlUmmahNow.FileScanService)**:

1. **ASP.NET Core 8.0+**: Web API with SignalR support
2. **SQL Server**: Database for watch and approval storage
3. **Entity Framework Core**: ORM for data access
4. **File System Access**: Read permissions on watched directories

**Startup Configuration** (Program.cs):
```csharp
// Add SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
});

// Add services
builder.Services.AddSingleton<IFileWatchService, FileWatchService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();

// Add DbContext
builder.Services.AddDbContext<ImportDbContext>(options =>
    options.UseSqlServer(connectionString));

// Map SignalR hub
app.MapHub<FileScanHub>("/hubs/filescan");
```

**Client Application (AlUmmahNow.VisualImporterUno)**:

1. **Uno Platform 5.0+**: Cross-platform UI framework
2. **Microsoft.AspNetCore.SignalR.Client**: SignalR client library
3. **CommunityToolkit.Mvvm**: MVVM toolkit for ViewModels
4. **Network Access**: HTTPS connection to FileScan service

**Dependency Injection** (App.xaml.cs):
```csharp
services.AddSingleton<FileScanSignalRClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["FileScanService:BaseUrl"];
    var hubPath = config["FileScanService:HubPath"];
    return new FileScanSignalRClient(baseUrl, hubPath);
});

services.AddTransient<WatchViewModel>();
services.AddTransient<ApprovalViewModel>();
```

### Environment Variables

**Development**:
```bash
ASPNETCORE_ENVIRONMENT=Development
FileScanService__BaseUrl=https://localhost:7001
ConnectionStrings__ImportDb="Server=localhost;Database=AlUmmahNow;Trusted_Connection=True;"
```

**Production**:
```bash
ASPNETCORE_ENVIRONMENT=Production
FileScanService__BaseUrl=https://filescan.alummahnow.com
ConnectionStrings__ImportDb="Server=prod-server;Database=AlUmmahNow;User=app_user;Password=***"
ASPNETCORE_URLS=https://+:443;http://+:80
```

### Security Considerations

**Authentication**: (Future Enhancement)
- Add JWT token authentication to SignalR hub
- Implement user-based authorization for approval decisions
- Role-based access control (e.g., Approver, Admin roles)

**Network Security**:
- Use HTTPS/WSS for SignalR connections
- Configure CORS policies appropriately
- Implement rate limiting on approval endpoints

**File System Security**:
- Validate watched paths are within allowed directories
- Implement file size limits to prevent DoS
- Sanitize file paths to prevent directory traversal

---

## API Reference

### FileScanSignalRClient API

**Connection Management**:
```csharp
class FileScanSignalRClient
{
    // Connection
    Task<bool> ConnectAsync()
    Task DisconnectAsync()
    bool IsConnected { get; }

    // Properties
    ServiceStatus CurrentStatus { get; }
    string? LastError { get; }
}
```

**Watch Management Methods**:
```csharp
// Start a new watch
Task<Guid> StartWatchAsync(CreateWatchRequest request)

// Stop an active watch
Task<bool> StopWatchAsync(Guid watchId)

// Get all active watches
Task<List<WatchStatusDto>> GetActiveWatchesAsync()

// Get watch status by ID
Task<WatchStatusDto?> GetWatchStatusAsync(Guid watchId)

// Update watch configuration
Task<bool> UpdateWatchAsync(Guid watchId, CreateWatchRequest request)

// Delete a watch
Task<bool> DeleteWatchAsync(Guid watchId)
```

**Approval Management Methods**:
```csharp
// Get all pending approvals
Task<List<PendingApprovalDto>> GetPendingApprovalsAsync()

// Get pending approvals for specific channel
Task<List<PendingApprovalDto>> GetPendingApprovalsByChannelAsync(string channelName)

// Process approval decision
Task<bool> ProcessApprovalDecisionAsync(ApprovalDecisionRequest request)

// Process bulk approval with filters
Task<int> ProcessBulkApprovalAsync(BulkApprovalRequest request)

// Get approval statistics
Task<ApprovalStatistics> GetApprovalStatisticsAsync(Guid? channelWatchId = null)
```

### SignalR Events (Client → Server)

**Hub Methods** (called by client):
```csharp
// Groups
Task JoinWatchGroup(string watchId)
Task LeaveWatchGroup(string watchId)
Task JoinApprovalsGroup()
Task LeaveApprovalsGroup()

// Watch operations
Task<Guid> StartWatch(CreateWatchRequest request)
Task<bool> StopWatch(Guid watchId)
Task<List<WatchStatusDto>> GetActiveWatches()
Task<bool> DeleteWatch(Guid watchId)

// Approval operations
Task<List<PendingApprovalDto>> GetPendingApprovals()
Task<bool> ProcessApprovalDecision(ApprovalDecisionRequest request)
Task<int> ProcessBulkApproval(BulkApprovalRequest request)
Task<ApprovalStatistics> GetApprovalStatistics(Guid? channelWatchId = null)
```

### SignalR Events (Server → Client)

**Connection Events**:
```csharp
// Server status changes
On<ServiceStatus>("ServiceStatusChanged", status => { })

// Error notifications
On<string>("ErrorOccurred", error => { })
```

**Watch Events**:
```csharp
// New file detected by watch
On<Guid, string, string, string, long>("FileDetected",
    (watchId, channelName, fileName, filePath, fileSize) => { })

// Watch status changed (started/stopped)
On<Guid, string, bool, string?>("WatchStatusChanged",
    (watchId, channelName, isActive, statusMessage) => { })

// Watch stopped
On<Guid>("WatchStopped", watchId => { })

// Watch configuration updated
On<Guid>("WatchUpdated", watchId => { })

// Watch deleted
On<Guid>("WatchDeleted", watchId => { })
```

**Approval Events**:
```csharp
// New file awaiting approval
On<Guid, string, string, string, long>("NewFileForApproval",
    (watchId, channelName, fileName, filePath, fileSize) => { })

// Approval status changed
On<Guid, string, string, ApprovalStatus, ApprovalStatus, string?>("ApprovalStatusChanged",
    (approvalId, channelName, fileName, oldStatus, newStatus, approvedBy) => { })

// Individual approval decision processed
On<ApprovalDecisionRequest>("ApprovalDecisionProcessed", request => { })

// Bulk approval processed
On<BulkApprovalRequest, int>("BulkApprovalProcessed",
    (request, processedCount) => { })
```

### Data Transfer Objects (DTOs)

**CreateWatchRequest**:
```csharp
class CreateWatchRequest
{
    string ChannelName          // Required, 2-100 chars, unique
    string WatchPath            // Required, valid directory path
    bool IncludeSubdirectories  // Default: true
    int ProcessingDelayMs       // Default: 5000 (5 seconds), range: 1000-300000
    string? FileExtensions      // Comma-separated, default: ".mp4,.mkv,..."
    string? CreatedBy           // Username who created watch
}
```

**WatchStatusDto**:
```csharp
class WatchStatusDto
{
    Guid WatchId
    string ChannelName
    string WatchPath
    bool IsActive
    DateTime CreatedAt
    DateTime? LastFileDetected
    int TotalFilesDetected
    int PendingApprovals
    int AutoApproved
    int ManuallyApproved
    int Rejected
    int Imported
    int Failed
}
```

**PendingApprovalDto**:
```csharp
class PendingApprovalDto
{
    Guid Id
    string ChannelName
    string FileName
    string FilePath
    long FileSize
    string Extension
    DateTime DetectedAt
    DateTime FileModifiedAt
    ApprovalStatus Status       // Pending, Approved, Rejected, etc.
    int Priority                // 1-5 (1=highest)
    bool FileExists
    string? ThumbnailPath
    DateTime? ExpiresAt
    string FileSizeFormatted    // Calculated property (e.g., "1.2 GB")
}
```

**ApprovalDecisionRequest**:
```csharp
class ApprovalDecisionRequest
{
    List<Guid> ApprovalIds      // Required, list of approval IDs to process
    ApprovalStatus Decision     // Required, Approved or Rejected
    string? Notes               // Optional notes
    string? ApprovedBy          // Username making decision
}
```

**BulkApprovalRequest**:
```csharp
class BulkApprovalRequest
{
    Guid? ChannelWatchId        // Optional, specific watch ID
    string? ChannelName         // Optional, channel name filter
    ApprovalStatus Decision     // Required, Approved or Rejected
    DateTime? DetectedAfter     // Optional, min detection date
    DateTime? DetectedBefore    // Optional, max detection date
    List<string>? FileExtensions // Optional, list of extensions (e.g., ["mp4", "mkv"])
    long? MinFileSize           // Optional, minimum file size in bytes
    long? MaxFileSize           // Optional, maximum file size in bytes
    string? Notes               // Optional notes
    string? ApprovedBy          // Username making decision
}
```

**ApprovalStatistics**:
```csharp
class ApprovalStatistics
{
    int PendingCount            // Total pending approvals
    int ApprovedCount           // Total approved
    int RejectedCount           // Total rejected
    int AutoApprovedCount       // Total auto-approved
    int ExpiredCount            // Total expired
    int ImportedCount           // Total imported
    int FailedCount             // Total failed imports
    DateTime? OldestPending     // Timestamp of oldest pending approval
    DateTime? NewestPending     // Timestamp of newest pending approval
    long TotalSize              // Total size in bytes (all statuses)
    long TotalSizePending       // Total size of pending files
    long TotalSizeApproved      // Total size of approved files

    string TotalSizeFormatted            { get; } // Formatted string
    string TotalSizePendingFormatted     { get; } // Formatted string
    string TotalSizeApprovedFormatted    { get; } // Formatted string
}
```

**ApprovalStatus Enum**:
```csharp
enum ApprovalStatus
{
    Pending = 0,         // Awaiting user decision
    Approved = 1,        // Approved for import
    Rejected = 2,        // Rejected - will not be imported
    AutoApproved = 3,    // Automatically approved (future feature)
    Expired = 4,         // Expired without decision
    ImportStarted = 5,   // Import in progress
    ImportCompleted = 6, // Successfully imported
    ImportFailed = 7     // Import failed
}
```

### Event Arguments Classes

**FileDetectedEventArgs**:
```csharp
class FileDetectedEventArgs : EventArgs
{
    Guid WatchId
    Guid ChannelWatchId
    string ChannelName
    string FilePath
    string FileName
    long FileSize
    DateTime DetectedAt
}
```

**WatchStatusChangedEventArgs**:
```csharp
class WatchStatusChangedEventArgs : EventArgs
{
    Guid WatchId
    string ChannelName
    bool IsActive
    string? StatusMessage
}
```

**ApprovalStatusChangedEventArgs**:
```csharp
class ApprovalStatusChangedEventArgs : EventArgs
{
    Guid ApprovalId
    Guid ChannelWatchId
    string ChannelName
    string FileName
    ApprovalStatus OldStatus
    ApprovalStatus NewStatus
    string? ApprovedBy
}
```

**NewFileForApprovalEventArgs**:
```csharp
class NewFileForApprovalEventArgs : EventArgs
{
    Guid WatchId
    string ChannelName
    string FileName
    string FilePath
    long FileSize
}
```

**BulkApprovalProcessedEventArgs**:
```csharp
class BulkApprovalProcessedEventArgs : EventArgs
{
    BulkApprovalRequest Request
    int ProcessedCount
    ApprovalStatus Decision
}
```

---

## Troubleshooting

### Common Issues

#### 1. SignalR Connection Fails

**Symptoms**:
- Status message: "Not connected to FileScan service"
- Watch/approval operations fail with connection errors
- UI shows "Disconnected" status

**Possible Causes**:
- Backend service not running
- Incorrect service URL in configuration
- Network/firewall blocking connection
- SSL certificate issues (in development)

**Solutions**:

1. **Verify Backend Service**:
   ```bash
   # Check if service is running
   curl https://localhost:7001/hubs/filescan
   # Should return 404 (hub endpoint is WebSocket only)
   ```

2. **Check Configuration**:
   ```csharp
   // Verify User Secrets or appsettings.json
   {
     "FileScanService": {
       "BaseUrl": "https://localhost:7001",  // Correct URL?
       "HubPath": "/hubs/filescan"           // Correct path?
     }
   }
   ```

3. **Inspect Browser/App Console**:
   - Look for CORS errors
   - Check for SSL/TLS handshake failures
   - Verify WebSocket upgrade successful

4. **Development SSL Issues**:
   ```bash
   # Trust development certificate
   dotnet dev-certs https --trust
   ```

5. **Enable Detailed Logging**:
   ```csharp
   // In FileScanSignalRClient constructor
   _connection = new HubConnectionBuilder()
       .WithUrl(hubUrl)
       .ConfigureLogging(logging =>
       {
           logging.SetMinimumLevel(LogLevel.Debug);
           logging.AddConsole();
       })
       .Build();
   ```

#### 2. Files Not Detected by Watch

**Symptoms**:
- New files added to watched directory don't appear in approval queue
- TotalFilesDetected count doesn't increase
- No FileDetected events received

**Possible Causes**:
- Watch is not active (IsActive = false)
- File extension not in configured list
- Processing delay hasn't elapsed
- Insufficient file system permissions
- File being copied/written when detection fires
- FileSystemWatcher buffer overflow (too many files at once)

**Solutions**:

1. **Verify Watch Status**:
   - Check IsActive in WatchManagementPage
   - Look for error messages in watch StatusMessage
   - Ensure watch hasn't been stopped

2. **Check File Extension**:
   ```csharp
   // Watch configuration
   FileExtensions = ".mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm"

   // Ensure your file extension is in this list
   // Extensions are case-insensitive
   ```

3. **Wait for Processing Delay**:
   - Default delay is 5 seconds
   - File won't be detected until delay elapses
   - This prevents detecting files mid-copy

4. **Verify File System Permissions**:
   ```bash
   # Windows: Check directory permissions
   icacls "C:\Videos\Import"

   # Linux/Mac: Check permissions
   ls -la /path/to/watched/directory
   ```

5. **Check Backend Logs**:
   - Look for FileSystemWatcher errors
   - Check for exceptions during file processing
   - Verify file hash calculation succeeds

6. **Restart Watch**:
   - Stop and restart the watch
   - FileSystemWatcher can sometimes become unresponsive

#### 3. Approval Actions Don't Update UI

**Symptoms**:
- Click Approve/Reject but file remains in pending list
- Statistics don't update
- Activity log shows no message
- No error displayed

**Possible Causes**:
- SignalR connection lost (events not received)
- Database update failed
- Filter hiding the updated file
- UI thread synchronization issue

**Solutions**:

1. **Check Connection Status**:
   - Verify "Connected to FileScan service" in status message
   - Try clicking Refresh button
   - Reconnect if necessary

2. **Review Activity Log**:
   - Look for error messages
   - Check if "Approved X files" message appears
   - Review backend logs for exceptions

3. **Check Filters**:
   - If ShowOnlyPending = true, approved files disappear immediately (expected)
   - Toggle ShowOnlyPending to see all files
   - Clear all filters to see full list

4. **Manual Refresh**:
   - Click Refresh button to force data reload
   - This bypasses SignalR events and queries database directly

5. **Inspect Database**:
   ```sql
   SELECT Id, FileName, Status, ApprovedAt, ApprovedBy
   FROM PendingApprovals
   WHERE Id = '<approval-guid>'
   ```

6. **Check DispatcherQueue**:
   - UI updates must occur on UI thread
   - Check console for threading exceptions
   - Verify DispatcherQueue is properly initialized

#### 4. Bulk Approval Processes Wrong Files

**Symptoms**:
- Bulk approve/reject affects more or fewer files than expected
- Wrong files approved/rejected
- ProcessedCount doesn't match filter count

**Possible Causes**:
- Filters not applied correctly
- Date/time filters affected by timezone
- Case sensitivity in extension filters
- Filter state changed during processing

**Solutions**:

1. **Preview Filters Before Bulk Operation**:
   - Check FilteredApprovals count before clicking Bulk Approve/Reject
   - Review visible files to ensure they match expectations
   - Test filters with small dataset first

2. **Review Filter Values**:
   ```csharp
   // Date filters: ensure correct timezone
   DateFromFilter = DateTime.Today.AddDays(-7); // Last 7 days

   // Extension filter: case-insensitive, comma-separated
   ExtensionFilter = "mp4,mkv"; // Correct
   ExtensionFilter = ".mp4,.mkv"; // Also works (dot optional)

   // Size filters: in bytes
   MinSizeFilter = 524288000; // 500 MB
   ```

3. **Check Backend Filter Logic**:
   - Bulk operations use the same filters as client
   - Verify BulkApprovalRequest is correctly built
   - Review ApprovalService.ProcessBulkApprovalAsync() logs

4. **Use Selection-Based Approach**:
   - Instead of bulk filters, use checkboxes to select specific files
   - Click "Approve Selected" for precise control
   - Safer for critical operations

#### 5. Performance Issues with Large File Counts

**Symptoms**:
- UI becomes slow/unresponsive with many files
- ListView scrolling lags
- Filter/sort operations take seconds
- High memory usage

**Possible Causes**:
- Loading too many records at once
- ListView virtualization not working
- Complex data bindings
- SignalR events overwhelming UI thread

**Solutions**:

1. **Implement Pagination**:
   ```csharp
   // Modify GetPendingApprovalsAsync to support paging
   Task<List<PendingApprovalDto>> GetPendingApprovalsAsync(
       int skip = 0,
       int take = 100
   )
   ```

2. **Optimize Filters**:
   - Use server-side filtering instead of client-side
   - Reduce FilteredApprovals size before binding
   - Consider lazy loading for large lists

3. **Limit Activity Log Size**:
   ```csharp
   // Already implemented in ApprovalViewModel
   while (ActivityLog.Count > 200)
       ActivityLog.RemoveAt(ActivityLog.Count - 1);
   ```

4. **Database Indexing**:
   ```sql
   -- Add indexes for common query patterns
   CREATE INDEX IX_PendingApprovals_Status ON PendingApprovals(Status);
   CREATE INDEX IX_PendingApprovals_ChannelName ON PendingApprovals(ChannelName);
   CREATE INDEX IX_PendingApprovals_DetectedAt ON PendingApprovals(DetectedAt DESC);
   ```

5. **Debounce Filter Changes**:
   ```csharp
   // Avoid applying filters on every keystroke
   private CancellationTokenSource? _filterDebounceToken;

   partial void OnChannelFilterChanged(string? value)
   {
       _filterDebounceToken?.Cancel();
       _filterDebounceToken = new CancellationTokenSource();

       Task.Delay(500, _filterDebounceToken.Token)
           .ContinueWith(_ => ApplyFiltersAndSorting(),
               TaskScheduler.FromCurrentSynchronizationContext());
   }
   ```

### Debugging Tips

#### Enable Verbose Logging

**Backend (appsettings.Development.json)**:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore.SignalR": "Debug",
      "AlUmmahNow.FileScanService": "Debug"
    }
  }
}
```

**Client (FileScanSignalRClient)**:
```csharp
_connection = new HubConnectionBuilder()
    .WithUrl(_hubUrl)
    .ConfigureLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Trace);
        logging.AddDebug();
    })
    .WithAutomaticReconnect()
    .Build();
```

#### Monitor SignalR Traffic

**Browser DevTools**:
1. Open DevTools (F12)
2. Navigate to Network tab
3. Filter: WS (WebSocket)
4. Click SignalR connection
5. View Messages tab for real-time traffic

**Backend Logging**:
```csharp
// In FileScanHub
_logger.LogDebug("Sending FileDetected: WatchId={WatchId}, File={FileName}",
    watchId, fileName);
```

#### Inspect Database State

```sql
-- Check active watches
SELECT Id, ChannelName, WatchPath, IsActive, TotalFilesDetected
FROM ChannelWatches
ORDER BY CreatedAt DESC;

-- Check pending approvals
SELECT TOP 100
    Id, FileName, ChannelName, Status, FileSize, DetectedAt
FROM PendingApprovals
ORDER BY DetectedAt DESC;

-- Approval statistics
SELECT
    Status,
    COUNT(*) AS Count,
    SUM(FileSize) AS TotalSize
FROM PendingApprovals
GROUP BY Status;

-- Find stuck/orphaned records
SELECT *
FROM PendingApprovals
WHERE Status = 'Pending'
  AND DetectedAt < DATEADD(day, -30, GETDATE());
```

#### Test SignalR Connection Manually

**Using SignalR Client Tool**:
```bash
# Install tool
npm install -g @microsoft/signalr-client

# Connect to hub
signalr-client -u https://localhost:7001/hubs/filescan

# Invoke method
> invoke GetPendingApprovals

# Subscribe to event
> on FileDetected
```

#### Common Error Messages

| Error | Cause | Solution |
|-------|-------|----------|
| "HubException: Failed to start watch: Directory does not exist" | Invalid watch path | Verify directory exists on server |
| "Connection closed with an error: WebSocket closed with status code: 1006" | Backend crashed or forcefully closed | Check backend logs for exceptions |
| "The invocation 'ProcessApprovalDecision' has not completed yet" | Long-running operation timeout | Increase SignalR timeout, optimize query |
| "The client is not connected to the hub" | SignalR connection lost | Call ConnectAsync() before operations |
| "Cannot invoke 'StartWatch' because the connection is not in the 'Connected' state" | Attempting operations while connecting | Wait for IsConnected = true |

### Support Resources

**Log Locations**:
- Backend: Console output or configured log sink (e.g., Application Insights)
- Client: Debug output window (Visual Studio), platform-specific logs

**Diagnostic Commands**:
```bash
# Check backend health
curl https://localhost:7001/health

# View active SignalR connections (if endpoint exists)
curl https://localhost:7001/api/diagnostics/signalr-connections

# Database connection test
dotnet ef database update --project AlUmmahNow.Data
```

**Community Support**:
- GitHub Issues: [Repository URL]
- Documentation: [Wiki URL]
- Discord/Slack: [Community Channel]

---

## Appendix

### Database Schema

**ChannelWatches Table**:
```sql
CREATE TABLE ChannelWatches (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ChannelName NVARCHAR(500) NOT NULL,
    WatchPath NVARCHAR(2000) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    IncludeSubdirectories BIT NOT NULL DEFAULT 1,
    ProcessingDelayMs INT NOT NULL DEFAULT 5000,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastUpdated DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(256) NULL,
    LastFileDetected DATETIME2 NULL,
    TotalFilesDetected INT NOT NULL DEFAULT 0,
    FileExtensions NVARCHAR(500) NOT NULL DEFAULT '.mp4,.mkv,.avi,.mov,.wmv,.m4v,.flv,.webm',

    CONSTRAINT UK_ChannelWatches_ChannelName UNIQUE (ChannelName),
    CONSTRAINT UK_ChannelWatches_WatchPath UNIQUE (WatchPath),
    INDEX IX_ChannelWatches_IsActive (IsActive),
    INDEX IX_ChannelWatches_CreatedAt (CreatedAt DESC)
);
```

**PendingApprovals Table**:
```sql
CREATE TABLE PendingApprovals (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ChannelWatchId UNIQUEIDENTIFIER NOT NULL,
    FilePath NVARCHAR(2000) NOT NULL,
    FileName NVARCHAR(500) NOT NULL,
    FileSize BIGINT NOT NULL,
    Extension NVARCHAR(10) NOT NULL,
    DetectedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    FileModifiedAt DATETIME2 NOT NULL,
    Status INT NOT NULL DEFAULT 0, -- ApprovalStatus enum
    ApprovedAt DATETIME2 NULL,
    ApprovedBy NVARCHAR(256) NULL,
    ApprovalNotes NVARCHAR(1000) NULL,
    FileExists BIT NOT NULL DEFAULT 1,
    ContentHash NVARCHAR(64) NULL,
    ThumbnailPath NVARCHAR(2000) NULL,
    Priority INT NOT NULL DEFAULT 3,
    ExpiresAt DATETIME2 NULL,

    CONSTRAINT FK_PendingApprovals_ChannelWatches
        FOREIGN KEY (ChannelWatchId) REFERENCES ChannelWatches(Id) ON DELETE CASCADE,
    INDEX IX_PendingApprovals_Status (Status),
    INDEX IX_PendingApprovals_ChannelWatchId (ChannelWatchId),
    INDEX IX_PendingApprovals_DetectedAt (DetectedAt DESC),
    INDEX IX_PendingApprovals_ContentHash (ContentHash)
);
```

### Future Enhancements

1. **Auto-Approval Rules**:
   - Define rules for automatic approval based on criteria
   - Example: Auto-approve files from trusted channels over X size
   - Implement rule engine with configurable conditions

2. **File Preview**:
   - Generate video thumbnails on detection
   - Extract metadata (duration, resolution, codec)
   - Display preview in approval workflow

3. **Advanced Search**:
   - Full-text search on filenames
   - Search by content hash (find duplicates)
   - Saved search filters

4. **Notifications**:
   - Email/SMS notifications for new files
   - Daily digest of pending approvals
   - Slack/Teams integration

5. **Approval Comments**:
   - Add comments to approval decisions
   - Discussion thread per file
   - @ mentions for collaboration

6. **Import Scheduling**:
   - Schedule approved files for import at specific times
   - Prioritize imports based on priority field
   - Batch import processing

7. **Mobile App**:
   - Native mobile UI for approval workflow
   - Push notifications for new files
   - Quick approve/reject gestures

8. **Machine Learning**:
   - Auto-categorize files based on content
   - Suggest approval decisions based on patterns
   - Anomaly detection (unusually large files, suspicious names)

### Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-01-XX | Initial release of Watch and Approval features |
| | | - Basic watch management |
| | | - Approval workflow with filtering |
| | | - SignalR real-time updates |
| | | - Statistics dashboard |

---

**Document Information**:
- **Created**: January 2025
- **Last Updated**: January 2025
- **Authors**: AlUmmahNow Development Team
- **Project**: AlUmmahNow Visual Importer (Uno Platform)
- **Repository**: /home/meywd/AlUmmahNowTools
