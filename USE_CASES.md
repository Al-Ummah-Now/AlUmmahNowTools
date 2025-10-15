# AlUmmahNow Visual Importer - Use Case Documentation

## Table of Contents

1. [Watch Management Feature](#1-watch-management-feature)
2. [Approval Workflow Feature](#2-approval-workflow-feature)
3. [Dashboard/Main View](#3-dashboardmain-view)
4. [Import Features](#4-import-features)
5. [Service Status](#5-service-status)
6. [File Discovery](#6-file-discovery)
7. [Analytics Dashboard](#7-analytics-dashboard)
8. [Settings Management](#8-settings-management)

---

## 1. Watch Management Feature

*Source Files: `WatchManagementPage.xaml`, `WatchViewModel.cs`*

### UC-001: Create Single Watch

**Actor**: System Administrator
**Goal**: Create a new file system watch to monitor a specific directory for video files
**Preconditions**:
- User is authenticated and on Watch Management page
- FileScan service is connected and available

**Trigger**: User clicks "Create Watch" button

### Main Flow:
1. System displays Create Watch dialog
2. User enters channel name (e.g., "Islamic Lectures")
3. User specifies directory path to watch
4. User configures processing delay (1-60 seconds)
5. User selects whether to include subdirectories
6. User specifies file extensions to monitor (.mp4, .mkv, etc.)
7. System validates all inputs in real-time
8. User clicks "Create Watch"
9. System creates watch via SignalR connection
10. System displays success notification
11. New watch appears in Active Watches list

### Alternative Flows:

**Alt-1: Browse for Directory**
3a. User clicks "Browse" button
3b. System shows directory picker
3c. User navigates and selects directory
3d. System populates path field

**Alt-2: Use Default Extensions**
6a. System pre-populates common video extensions
6b. User accepts defaults

### Exception Flows:

**Exc-1: Channel Name Already Exists**
2e. System detects duplicate channel name
2f. System shows validation error
2g. User must choose different name

**Exc-2: Directory Does Not Exist**
3e. System validates path doesn't exist
3f. System shows validation error
3g. User must select valid directory

**Exc-3: Service Connection Lost**
9e. SignalR connection fails
9f. System shows connection error
9g. User must retry when connected

**Postconditions**:
- New watch is active and monitoring specified directory
- Watch appears in active watches list
- System begins detecting files in watched directory

**Business Rules**:
- Channel names must be unique
- Channel names must be 2-100 characters
- Processing delay must be 1-300 seconds
- At least one file extension must be specified
- Directory must exist and be accessible

**Technical Notes**:
- Real-time validation via `ValidateChannelName()` and `ValidateWatchPath()` methods
- Watch creation through `FileScanSignalRClient.StartWatchAsync()`
- UI updates via dispatcher queue for thread safety

---

### UC-002: Batch Create Watches from Template

**Actor**: System Administrator
**Goal**: Create multiple watches simultaneously using predefined templates
**Preconditions**:
- User is on Watch Management page
- Templates are available
- FileScan service is connected

**Trigger**: User clicks "Batch Create" button

### Main Flow:
1. System displays Batch Create dialog
2. User selects "From Template" tab
3. System loads available templates
4. User selects a template
5. User specifies number of watches (1-50)
6. User sets starting number for naming sequence
7. User configures base path
8. User clicks "Generate Preview"
9. System shows preview of watches to be created
10. User reviews preview
11. User clicks "Create All Watches"
12. System creates watches sequentially
13. System shows progress bar
14. System displays completion summary

### Alternative Flows:

**Alt-1: Save as New Template**
10a. User clicks "Save as Template"
10b. System prompts for template name
10c. User provides name and description
10d. System saves configuration as template

### Exception Flows:

**Exc-1: Template Generation Fails**
9e. System cannot generate preview
9f. System shows error message
9g. User must adjust parameters

**Exc-2: Partial Creation Success**
12e. Some watches fail to create
12f. System continues with remaining watches
12g. System reports success/failure count

**Postconditions**:
- Multiple watches created and active
- All successful watches appear in active list
- Failed watches reported to user

**Business Rules**:
- Maximum 50 watches per batch
- Template patterns must include placeholders
- Each generated watch must have unique name and path

**Technical Notes**:
- Template generation via `WatchTemplateService.GenerateWatchesFromTemplateAsync()`
- Progress tracking with `BatchCreateProgress` and `BatchCreateTotal`
- Batch creation continues on individual failures

---

### UC-003: Import Watches from File

**Actor**: System Administrator
**Goal**: Create multiple watches by importing configuration from CSV or JSON file
**Preconditions**:
- User has prepared import file in correct format
- File is accessible from application

**Trigger**: User selects "Import from File" in Batch Create dialog

### Main Flow:
1. User selects "Import from File" tab
2. User clicks "Browse" to select file
3. System validates file format (CSV or JSON)
4. User clicks "Import and Validate"
5. System parses file contents
6. System validates each watch configuration
7. System displays validation results
8. User reviews valid and invalid entries
9. User clicks "Create Valid Watches"
10. System creates all valid watches
11. System reports completion status

### Alternative Flows:

**Alt-1: Fix Validation Errors**
8a. User reviews validation errors
8b. User modifies import file
8c. User re-imports corrected file

### Exception Flows:

**Exc-1: Invalid File Format**
3e. File is not CSV or JSON
3f. System shows format error
3g. User must select correct file type

**Exc-2: All Entries Invalid**
7e. No valid watch configurations found
7f. System shows validation summary
7g. User must fix import file

**Postconditions**:
- Valid watches from import are created
- Import results displayed to user
- Invalid entries documented for correction

**Business Rules**:
- CSV must have headers: ChannelName, WatchPath, Extensions
- JSON must follow WatchConfiguration schema
- All imported watches must pass validation rules

**Technical Notes**:
- Import handled by `WatchTemplateService.ImportFromCsvAsync/ImportFromJsonAsync`
- Validation per watch using same rules as single creation
- Batch creation with progress tracking

---

### UC-004: Monitor Active Watches

**Actor**: System Administrator
**Goal**: View and monitor status of all active watches
**Preconditions**: Active watches exist in system

**Trigger**: User navigates to Watch Management page

### Main Flow:
1. System loads all active watches
2. System displays watch statistics dashboard
3. System shows active watches list
4. User views watch details:
   - Channel name
   - Watch path
   - Active status indicator
   - Files detected count
   - Pending approvals count
5. System updates counts in real-time via SignalR

### Alternative Flows:

**Alt-1: Filter by Channel**
3a. User selects channel filter
3b. System shows only watches for selected channel

**Alt-2: Sort Watches**
3a. User clicks sort option
3b. System reorders watch list

### Exception Flows:

**Exc-1: SignalR Disconnection**
5e. Real-time updates stop
5f. System shows disconnection warning
5g. Manual refresh required

**Postconditions**: User has current view of all watch statuses

**Business Rules**:
- Statistics update within 2 seconds of changes
- Inactive watches shown with different visual indicator
- File counts aggregate from watch start time

**Technical Notes**:
- Real-time updates via SignalR events: `FileDetected`, `WatchStatusChanged`
- Statistics calculated in `UpdateStatistics()` method
- ObservableCollection binding for automatic UI updates

---

### UC-005: Stop Active Watch

**Actor**: System Administrator
**Goal**: Temporarily stop monitoring a specific directory
**Preconditions**: Watch is currently active

**Trigger**: User clicks "Stop" button on watch item

### Main Flow:
1. User selects watch from active list
2. User clicks "Stop" button
3. System sends stop command via SignalR
4. System updates watch status to inactive
5. System shows success notification
6. Watch status indicator changes to inactive

### Alternative Flows:

**Alt-1: Stop Multiple Watches**
1a. User selects multiple watches
1b. User clicks bulk stop action
1c. System stops all selected watches

### Exception Flows:

**Exc-1: Stop Command Fails**
3e. SignalR command fails
3f. System shows error message
3g. Watch remains active

**Postconditions**:
- Watch is stopped and not monitoring
- Status updated in UI
- No new files detected from this watch

**Business Rules**:
- Only active watches can be stopped
- Stopped watches retain configuration
- Stopped watches can be restarted

**Technical Notes**:
- Stop command via `FileScanSignalRClient.StopWatchAsync()`
- Status update through `OnWatchStopped` event handler
- UI updates via dispatcher queue

---

## 2. Approval Workflow Feature

*Source Files: `ApprovalWorkflowPage.xaml`, `ApprovalViewModel.cs`*

### UC-006: Review Pending Approvals

**Actor**: Content Reviewer
**Goal**: Review files detected by watches awaiting approval
**Preconditions**:
- Pending approval files exist
- User has reviewer permissions

**Trigger**: User navigates to Approval Workflow page

### Main Flow:
1. System loads all pending approvals
2. System displays statistics dashboard:
   - Total pending count
   - Approved count
   - Rejected count
   - Total size pending
3. System shows filtered approval list
4. User reviews file information:
   - File name
   - Channel name
   - File size
   - Detection time
   - Priority level
   - Extension type
5. User can preview files
6. User continues reviewing files

### Alternative Flows:

**Alt-1: Apply Filters**
3a. User configures filters (channel, priority, size, date, extension)
3b. System updates displayed list
3c. System saves filter configuration

**Alt-2: Sort Results**
3a. User selects sort criteria
3b. User toggles sort direction
3c. System reorders list

### Exception Flows:

**Exc-1: Loading Fails**
1e. Service unavailable
1f. System shows error message
1g. User must retry

**Postconditions**: User has reviewed pending files

**Business Rules**:
- Only pending status files shown by default
- Filters persist within session
- Real-time updates when new files detected

**Technical Notes**:
- Approvals loaded via `FileScanSignalRClient.GetPendingApprovalsAsync()`
- Filtering logic in `ApplyFiltersAndSorting()` method
- Real-time updates via SignalR events

---

### UC-007: Approve Individual File

**Actor**: Content Reviewer
**Goal**: Approve a specific file for import
**Preconditions**: File is in pending status

**Trigger**: User clicks "Approve" on file item

### Main Flow:
1. User selects file from pending list
2. User reviews file details
3. User clicks "Approve" button
4. System processes approval decision
5. System updates file status to approved
6. File removed from pending list
7. System shows success notification
8. Activity logged in activity panel

### Alternative Flows:

**Alt-1: Approve with Preview**
2a. User clicks "Preview" first
2b. System generates video thumbnail
2c. System shows video metadata
2d. User reviews visual preview
2e. User proceeds with approval

### Exception Flows:

**Exc-1: Approval Fails**
4e. Processing error occurs
4f. System shows error message
4g. File remains pending

**Postconditions**:
- File status changed to approved
- File ready for import processing
- Approval logged with timestamp and user

**Business Rules**:
- Only pending files can be approved
- Approval requires user identification
- Approved files cannot be re-approved

**Technical Notes**:
- Approval via `FileScanSignalRClient.ProcessApprovalDecisionAsync()`
- Thumbnail generation using `VideoThumbnailService`
- Metadata extraction via FFMpegCore

---

### UC-008: Reject Individual File

**Actor**: Content Reviewer
**Goal**: Reject a file from being imported
**Preconditions**: File is in pending status

**Trigger**: User clicks "Reject" on file item

### Main Flow:
1. User selects file from pending list
2. User reviews file details
3. User clicks "Reject" button
4. System processes rejection decision
5. System updates file status to rejected
6. File removed from pending list
7. System shows rejection notification
8. Activity logged in activity panel

### Alternative Flows:

**Alt-1: Reject with Reason**
3a. System prompts for rejection reason
3b. User provides reason text
3c. Reason stored with rejection

### Exception Flows:

**Exc-1: Rejection Fails**
4e. Processing error occurs
4f. System shows error message
4g. File remains pending

**Postconditions**:
- File status changed to rejected
- File will not be imported
- Rejection logged with timestamp and user

**Business Rules**:
- Only pending files can be rejected
- Rejected files can be reconsidered later
- Rejection reasons are optional but recommended

**Technical Notes**:
- Rejection via `FileScanSignalRClient.ProcessApprovalDecisionAsync()`
- Status set to `ApprovalStatus.Rejected`
- Activity logging in `AddLog()` method

---

### UC-009: Bulk Approve Multiple Files

**Actor**: Content Reviewer
**Goal**: Approve multiple files simultaneously
**Preconditions**: Multiple pending files exist

**Trigger**: User clicks "Approve Selected" or "Bulk Approve All"

### Main Flow:
1. User selects multiple files using checkboxes
2. Selection count displayed in header
3. User clicks "Approve Selected"
4. System confirms bulk action
5. System processes all approvals
6. System updates file statuses
7. Files removed from pending list
8. System shows success summary
9. Activity logged for bulk operation

### Alternative Flows:

**Alt-1: Select All**
1a. User clicks "Select All"
1b. All visible files selected
1c. User proceeds with bulk approve

**Alt-2: Approve All Filtered**
1a. User applies filters first
1b. User clicks "Bulk Approve All"
1c. All files matching filters approved

### Exception Flows:

**Exc-1: Partial Success**
5e. Some approvals fail
5f. System processes successful ones
5g. System reports success/failure count

**Postconditions**:
- Selected files approved
- Bulk operation logged
- Statistics updated

**Business Rules**:
- Maximum 500 files per bulk operation
- Confirmation required for >50 files
- Failed items don't affect successful ones

**Technical Notes**:
- Bulk processing via `FileScanSignalRClient.ProcessBulkApprovalAsync()`
- Selection tracking in `SelectedApprovals` collection
- Progress indication during processing

---

### UC-010: Advanced Filtering

**Actor**: Content Reviewer
**Goal**: Filter pending approvals to focus on specific files
**Preconditions**: Approvals exist in system

**Trigger**: User interacts with filter controls

### Main Flow:
1. User opens filter panel
2. User selects filter criteria:
   - Channel name
   - Priority level (1-5)
   - File size range (min/max)
   - Date range (from/to)
   - File extensions
3. System applies filters in real-time
4. System updates approval list
5. System shows filter summary
6. User reviews filtered results

### Alternative Flows:

**Alt-1: Save as Preset**
6a. User clicks "Save Current"
6b. User provides preset name
6c. User sets description
6d. System saves filter configuration
6e. Preset available for future use

**Alt-2: Load Preset**
2a. User selects saved preset
2b. User clicks "Load"
2c. System applies preset filters
2d. List updates automatically

### Exception Flows:

**Exc-1: No Matches**
3e. No files match criteria
3f. System shows empty state
3g. User adjusts filters

**Postconditions**:
- Filtered view active
- Filter state persisted in session
- Results match all criteria

**Business Rules**:
- All filters combine with AND logic
- Empty filter means no restriction
- Date filters use local timezone

**Technical Notes**:
- Filter logic in `ApplyFiltersAndSorting()` method
- Presets stored via `IFilterPresetService`
- Real-time filtering on property changes

---

### UC-011: File Preview with Metadata

**Actor**: Content Reviewer
**Goal**: Preview video file before making approval decision
**Preconditions**: Video file exists and is accessible

**Trigger**: User clicks "Preview" button on file

### Main Flow:
1. User clicks preview on file item
2. System loads file information
3. System generates video thumbnail
4. System extracts video metadata:
   - Duration
   - Resolution
   - Codec
   - Frame rate
   - Bit rate
5. System displays preview panel:
   - Thumbnail image
   - Video information
   - File details
6. User reviews preview
7. User makes approval decision

### Alternative Flows:

**Alt-1: Refresh Thumbnail**
5a. Thumbnail unclear or missing
5b. User clicks "Refresh"
5c. System regenerates thumbnail
5d. New thumbnail displayed

### Exception Flows:

**Exc-1: File Not Accessible**
2e. File path invalid or inaccessible
2f. System shows error message
2g. Preview unavailable

**Exc-2: Metadata Extraction Fails**
4e. FFmpeg error occurs
4f. System shows partial information
4g. User proceeds without full metadata

**Postconditions**:
- User has visual preview of content
- Metadata available for decision
- Thumbnail cached for performance

**Business Rules**:
- Thumbnail taken from 10% into video
- Maximum thumbnail generation time: 5 seconds
- Metadata cached for session

**Technical Notes**:
- Thumbnail via `VideoThumbnailService.GenerateThumbnailAsync()`
- Metadata via `VideoThumbnailService.GetVideoMetadataAsync()`
- Async generation with loading indicator

---

## 3. Dashboard/Main View

*Source Files: `DashboardView.xaml`, `ModernDashboardView.xaml`*

### UC-012: View System Overview

**Actor**: Any User
**Goal**: Get quick overview of system status and activity
**Preconditions**: User is authenticated

**Trigger**: User logs in or navigates to dashboard

### Main Flow:
1. System displays main dashboard
2. System shows key metrics:
   - Active watches count
   - Pending approvals count
   - Recent activity
   - System status
3. System provides quick action buttons
4. User can navigate to specific features
5. Dashboard auto-refreshes every 30 seconds

### Alternative Flows:

**Alt-1: Manual Refresh**
5a. User clicks refresh button
5b. System updates all metrics
5c. Last update time shown

### Exception Flows:

**Exc-1: Service Unavailable**
2e. Cannot load metrics
2f. System shows connection error
2g. Retry option provided

**Postconditions**: User informed of system status

**Business Rules**:
- Dashboard is default landing page
- Metrics update automatically
- Quick actions based on user role

**Technical Notes**:
- Dashboard data aggregated from services
- Real-time updates via SignalR
- Responsive layout for different screens

---

## 4. Import Features

*Source Files: `ImportView.xaml`, `ModernImportView.xaml`*

### UC-013: Manual File Import

**Actor**: System Administrator
**Goal**: Manually import approved video files
**Preconditions**: Approved files exist

**Trigger**: User navigates to Import page

### Main Flow:
1. System shows approved files ready for import
2. User selects files to import
3. User configures import settings:
   - Target directory
   - Naming convention
   - Duplicate handling
4. User clicks "Start Import"
5. System processes file imports
6. System shows progress for each file
7. System displays completion summary
8. Imported files moved to target location

### Alternative Flows:

**Alt-1: Batch Import All**
2a. User clicks "Import All"
2b. All approved files selected
2c. Process continues from step 3

### Exception Flows:

**Exc-1: Import Fails**
5e. File copy/move fails
5f. System logs error
5g. Failed files remain in queue

**Postconditions**:
- Files imported to target location
- Import history logged
- Source files handled per configuration

**Business Rules**:
- Only approved files can be imported
- Duplicate files handled per policy
- Import maintains file metadata

**Technical Notes**:
- Import via `ImportService` class
- Progress tracking with events
- Transaction support for rollback

---

## 5. Service Status

*Source Files: `ServiceStatusPage.xaml`, `ServiceStatusViewModel.cs`*

### UC-014: Monitor Service Health

**Actor**: System Administrator
**Goal**: Monitor health and status of all system services
**Preconditions**: User has admin access

**Trigger**: User navigates to Service Status page

### Main Flow:
1. System displays all services:
   - FileScan Service
   - SignalR Connection
   - Database Service
   - Import Service
2. Each service shows:
   - Current status (Running/Stopped/Error)
   - Last heartbeat time
   - Memory usage
   - CPU usage
3. System updates status every 5 seconds
4. User can start/stop services
5. User can view service logs

### Alternative Flows:

**Alt-1: Restart Service**
4a. User clicks restart on service
4b. System stops service
4c. System starts service
4d. Status updates shown

### Exception Flows:

**Exc-1: Service Control Fails**
4e. Start/stop command fails
4f. System shows error details
4g. Manual intervention required

**Postconditions**: Admin aware of service health

**Business Rules**:
- Critical services marked in red when down
- Automatic restart configurable
- Alerts sent for service failures

**Technical Notes**:
- Health checks via endpoint monitoring
- Service control through Windows Service API
- Real-time updates via SignalR

---

## 6. File Discovery

*Source Files: `FileDiscoveryPage.xaml`*

### UC-015: Discover Existing Files

**Actor**: System Administrator
**Goal**: Scan directories for existing video files to import
**Preconditions**: Target directories are accessible

**Trigger**: User navigates to File Discovery page

### Main Flow:
1. User specifies directory to scan
2. User configures scan options:
   - Include subdirectories
   - File extensions
   - Size filters
   - Date filters
3. User clicks "Start Scan"
4. System scans directory recursively
5. System displays discovered files
6. User reviews discoveries
7. User selects files for import queue
8. Selected files added to approval workflow

### Alternative Flows:

**Alt-1: Save Scan Results**
6a. User exports results to CSV
6b. System generates report
6c. File saved to user location

### Exception Flows:

**Exc-1: Access Denied**
4e. Directory not accessible
4f. System shows permission error
4g. User must fix permissions

**Postconditions**:
- Discovered files catalogued
- Selected files queued for approval
- Scan history maintained

**Business Rules**:
- Large files (>4GB) require confirmation
- Hidden/system files excluded by default
- Scan can be cancelled mid-process

**Technical Notes**:
- Discovery via `FileDiscoveryService`
- Parallel scanning for performance
- Progress indication during scan

---

## 7. Analytics Dashboard

*Source Files: `AnalyticsDashboardPage.xaml`, `AnalyticsDashboardViewModel.cs`*

### UC-016: View Analytics and Reports

**Actor**: Manager/Administrator
**Goal**: View system analytics and performance metrics
**Preconditions**: Historical data exists

**Trigger**: User navigates to Analytics Dashboard

### Main Flow:
1. System displays analytics dashboard
2. User selects time period:
   - Today
   - This Week
   - This Month
   - All Time
3. System shows key metrics:
   - Total approvals
   - Approval rate
   - Average approval time
   - Files pending
   - Total size approved
   - Most active channel
4. System displays visualizations:
   - Approval trends chart
   - Priority distribution
   - Top channels
   - Peak hours heatmap
5. System shows recent activity list
6. User can export reports

### Alternative Flows:

**Alt-1: Custom Date Range**
2a. User selects custom range
2b. User picks start/end dates
2c. System updates all metrics

**Alt-2: Drill Down**
4a. User clicks on chart element
4b. System shows detailed view
4c. Additional metrics displayed

### Exception Flows:

**Exc-1: Insufficient Data**
3e. Not enough data for period
3f. System shows limited metrics
3g. Message explains requirements

**Postconditions**:
- User has analytical insights
- Reports available for export
- Decisions data-driven

**Business Rules**:
- Metrics calculate in user timezone
- Historical data retained 1 year
- Real-time updates when viewing today

**Technical Notes**:
- Analytics aggregated from database
- Charts rendered with data binding
- Export to CSV via `ExportReportCommand`

---

### UC-017: Export Analytics Report

**Actor**: Manager/Administrator
**Goal**: Export analytics data for external analysis
**Preconditions**: Analytics data is loaded

**Trigger**: User clicks "Export CSV" button

### Main Flow:
1. User views analytics dashboard
2. User applies desired filters/period
3. User clicks "Export CSV"
4. System generates CSV report:
   - Summary statistics
   - Detailed approval records
   - Channel breakdowns
   - Time-based metrics
5. System prompts for save location
6. User selects destination
7. System saves CSV file
8. Success notification shown

### Alternative Flows:

**Alt-1: Export to Excel**
3a. User selects Excel format
3b. System generates .xlsx file
3c. Formatting preserved

### Exception Flows:

**Exc-1: Export Fails**
4e. Generation error occurs
4f. System shows error message
4g. User retries export

**Postconditions**:
- Report file created
- Data exported successfully
- File ready for analysis

**Business Rules**:
- Export includes all visible data
- CSV follows RFC 4180 standard
- File names include timestamp

**Technical Notes**:
- Export via `ExportReportCommand`
- Async generation for large datasets
- Progress shown for long exports

---

## 8. Settings Management

*Source Files: `SettingsPage.xaml`, `ModernSettingsView.xaml`, `SettingsViewModel.cs`*

### UC-018: Configure Application Settings

**Actor**: System Administrator
**Goal**: Configure application behavior and preferences
**Preconditions**: User has admin privileges

**Trigger**: User navigates to Settings page

### Main Flow:
1. System displays settings categories:
   - General Settings
   - Watch Settings
   - Approval Settings
   - Import Settings
   - Notification Settings
2. User modifies settings:
   - Default paths
   - Timeout values
   - Notification preferences
   - UI themes
   - Auto-approval rules
3. User clicks "Save Settings"
4. System validates changes
5. System applies new settings
6. System shows confirmation
7. Changes take effect immediately

### Alternative Flows:

**Alt-1: Reset to Defaults**
3a. User clicks "Reset to Defaults"
3b. System confirms action
3c. All settings restored
3d. System restarts if needed

**Alt-2: Import Settings**
2a. User clicks "Import Settings"
2b. User selects settings file
2c. System loads configuration
2d. User reviews changes

### Exception Flows:

**Exc-1: Invalid Settings**
4e. Validation fails
4f. System shows validation errors
4g. User corrects values

**Exc-2: Save Fails**
5e. Cannot persist settings
5f. System shows error
5g. Settings remain unchanged

**Postconditions**:
- Settings persisted
- Application behavior updated
- Configuration backed up

**Business Rules**:
- Some settings require restart
- Validation ensures system stability
- Settings versioned for compatibility

**Technical Notes**:
- Settings stored in AppConfig
- Validation in ViewModel
- Hot-reload for most settings

---

### UC-019: Configure Auto-Approval Rules

**Actor**: System Administrator
**Goal**: Set up rules for automatic file approval
**Preconditions**: Admin access granted

**Trigger**: User accesses Auto-Approval section in settings

### Main Flow:
1. User navigates to Auto-Approval settings
2. User creates new rule:
   - Rule name
   - Channel selection
   - File size criteria
   - Extension filters
   - Time-based conditions
3. User sets rule priority
4. User enables/disables rule
5. User saves rule configuration
6. System validates rule logic
7. Rule becomes active
8. System auto-approves matching files

### Alternative Flows:

**Alt-1: Edit Existing Rule**
2a. User selects existing rule
2b. User modifies criteria
2c. User saves changes
2d. Rule updated immediately

**Alt-2: Test Rule**
5a. User clicks "Test Rule"
5b. System simulates on recent files
5c. Results preview shown
5d. User adjusts if needed

### Exception Flows:

**Exc-1: Conflicting Rules**
6e. Rule conflicts detected
6f. System shows conflicts
6g. User must resolve

**Postconditions**:
- Auto-approval rules active
- Matching files approved automatically
- Audit trail maintained

**Business Rules**:
- Rules evaluated in priority order
- First matching rule wins
- Manual override always possible
- All auto-approvals logged

**Technical Notes**:
- Rules engine evaluates in real-time
- Rule storage in database
- Performance optimized for high volume

---

## Cross-Cutting Use Cases

### UC-020: Handle Real-Time Updates

**Actor**: System
**Goal**: Keep UI synchronized with backend changes
**Preconditions**: SignalR connection established

**Trigger**: Backend event occurs

### Main Flow:
1. Backend service generates event
2. Event sent via SignalR hub
3. Client receives event notification
4. System processes event type:
   - FileDetected
   - ApprovalStatusChanged
   - WatchStatusChanged
   - StatisticsUpdated
5. System updates relevant UI components
6. User sees real-time changes
7. System logs event if configured

### Alternative Flows:

**Alt-1: Reconnection**
2a. Connection lost
2b. System attempts reconnection
2c. System queues updates
2d. Updates applied on reconnection

### Exception Flows:

**Exc-1: Update Fails**
5e. UI update error
5f. System logs error
5g. Manual refresh required

**Postconditions**: UI reflects current state

**Business Rules**:
- Updates within 2 seconds
- Automatic reconnection attempts
- Queued updates during disconnection

**Technical Notes**:
- SignalR for WebSocket communication
- Event handlers in ViewModels
- Dispatcher for thread-safe updates

---

### UC-021: Generate Notifications

**Actor**: System
**Goal**: Notify users of important events
**Preconditions**: Notifications enabled

**Trigger**: Significant event occurs

### Main Flow:
1. Event triggers notification:
   - New file detected
   - Approval completed
   - Error occurred
   - Service status changed
2. System checks notification preferences
3. System creates notification
4. System displays notification:
   - Toast notification
   - In-app notification
   - Sound alert (if enabled)
5. User acknowledges or dismisses
6. System logs notification

### Alternative Flows:

**Alt-1: Batch Notifications**
3a. Multiple events in short time
3b. System groups notifications
3c. Single summary shown

### Exception Flows:

**Exc-1: Notification Fails**
4e. Display error occurs
4f. System logs silently
4g. Event still processed

**Postconditions**: User informed of events

**Business Rules**:
- Critical errors always notify
- User preferences respected
- Notification history maintained

**Technical Notes**:
- NotificationService handles display
- Windows notification API integration
- Sound alerts configurable

---

## User Roles and Permissions

### Roles Defined:

1. **System Administrator**
   - Full system access
   - Service management
   - Settings configuration
   - User management

2. **Content Reviewer**
   - Approval workflow access
   - File preview capabilities
   - Cannot modify watches

3. **Viewer**
   - Read-only access
   - View dashboards
   - Export reports

### Permission Matrix:

| Feature | Admin | Reviewer | Viewer |
|---------|-------|----------|--------|
| Create Watch | ✓ | ✗ | ✗ |
| Stop Watch | ✓ | ✗ | ✗ |
| Approve Files | ✓ | ✓ | ✗ |
| View Analytics | ✓ | ✓ | ✓ |
| Configure Settings | ✓ | ✗ | ✗ |
| Export Reports | ✓ | ✓ | ✓ |
| Service Control | ✓ | ✗ | ✗ |

---

## System Integration Points

### External Systems:

1. **File System**
   - Directory monitoring
   - File operations
   - Permission management

2. **SignalR Hub**
   - Real-time communication
   - Event distribution
   - Status synchronization

3. **Database**
   - Persistent storage
   - Historical data
   - Configuration

4. **FFmpeg**
   - Video processing
   - Thumbnail generation
   - Metadata extraction

### Integration Patterns:

- **Event-Driven**: Real-time updates via SignalR
- **Request-Response**: API calls for data operations
- **Polling**: Service health checks
- **File Watching**: FileSystemWatcher for directories

---

## Performance Requirements

### Response Times:
- Page load: < 2 seconds
- Search/filter: < 500ms
- Thumbnail generation: < 5 seconds
- Bulk operations: Progress indication required

### Scalability:
- Support 100+ concurrent watches
- Handle 10,000+ pending approvals
- Process 1,000+ files per hour

### Reliability:
- 99.9% uptime for core services
- Automatic recovery from failures
- Data integrity maintained

---

## Security Considerations

### Authentication:
- Windows authentication integration
- Role-based access control
- Session management

### Authorization:
- Feature-level permissions
- Data-level security
- Audit logging

### Data Protection:
- Encrypted sensitive data
- Secure file operations
- Network communication over HTTPS

---

## Error Handling Patterns

### User Errors:
- Validation with helpful messages
- Confirmation for destructive actions
- Undo capabilities where possible

### System Errors:
- Graceful degradation
- Automatic retry logic
- Clear error messages
- Logging for diagnosis

### Recovery:
- Transaction rollback
- State restoration
- Manual intervention options

---

## Future Enhancements

### Planned Features:
1. Mobile application support
2. Advanced analytics with AI insights
3. Cloud storage integration
4. Multi-language support
5. Advanced workflow customization
6. API for third-party integration
7. Machine learning for auto-categorization
8. Advanced reporting capabilities

### Technical Debt:
- Optimize thumbnail generation
- Improve bulk operation performance
- Enhanced error recovery
- Better offline capabilities

---

## Documentation Maintenance

This document should be updated when:
- New features are added
- Workflows are modified
- Business rules change
- Technical implementation changes significantly

Last Updated: October 2024
Version: 1.0.0