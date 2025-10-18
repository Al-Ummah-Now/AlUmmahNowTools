using NUnit.Framework;
using Uno.UITest;
using AlUmmahNow.VisualImporterUno.UITests.Helpers;

namespace AlUmmahNow.VisualImporterUno.UITests.PageObjects;

/// <summary>
/// Page Object Model for Approval Workflow Page.
/// Encapsulates all UI interactions and element identifiers for the Approval Workflow feature.
/// </summary>
public class ApprovalWorkflowPageObject
{
    private readonly IApp _app;
    private readonly UITestHelpers _helpers;

    #region Element Identifiers

    // Page Elements
    public const string PageTitle = "ApprovalWorkflowPageTitle";
    public const string PageRoot = "PageRoot";

    // Header Actions
    public const string RefreshButton = "RefreshApprovalsButton";
    public const string SelectAllButton = "SelectAllButton";
    public const string ClearSelectionButton = "ClearSelectionButton";
    public const string ApproveSelectedButton = "ApproveSelectedButton";
    public const string RejectSelectedButton = "RejectSelectedButton";

    // Statistics Cards
    public const string PendingCard = "PendingStatCard";
    public const string ApprovedCard = "ApprovedStatCard";
    public const string RejectedCard = "RejectedStatCard";
    public const string TotalSizeCard = "TotalSizeStatCard";

    // Filters
    public const string FiltersSection = "FiltersSection";
    public const string ClearFiltersButton = "ClearFiltersButton";
    public const string ChannelFilterCombo = "ChannelFilterCombo";
    public const string PriorityFilterCombo = "PriorityFilterCombo";
    public const string MinSizeFilterInput = "MinSizeFilterInput";
    public const string MaxSizeFilterInput = "MaxSizeFilterInput";
    public const string DateFromPicker = "DateFromPicker";
    public const string DateToPicker = "DateToPicker";
    public const string ExtensionFilterInput = "ExtensionFilterInput";

    // Filter Presets
    public const string PresetSelector = "PresetSelector";
    public const string LoadPresetButton = "LoadPresetButton";
    public const string SavePresetButton = "SavePresetButton";
    public const string DeletePresetButton = "DeletePresetButton";
    public const string SetDefaultPresetButton = "SetDefaultPresetButton";

    // Approvals List
    public const string ApprovalsList = "ApprovalsList";
    public const string ApprovalsEmptyState = "ApprovalsEmptyState";
    public const string SortByCombo = "SortByCombo";
    public const string SortDirectionToggle = "SortDirectionToggle";

    // File Preview Panel
    public const string PreviewPanel = "PreviewPanel";
    public const string ThumbnailImage = "ThumbnailImage";
    public const string RefreshThumbnailButton = "RefreshThumbnailButton";
    public const string VideoMetadataSection = "VideoMetadataSection";
    public const string PreviewApproveButton = "PreviewApproveButton";
    public const string PreviewRejectButton = "PreviewRejectButton";

    // Activity Log
    public const string ActivityLog = "ActivityLog";
    public const string ClearActivityLogButton = "ClearActivityLogButton";
    public const string ActivityLogEmptyState = "ActivityLogEmptyState";

    // Bulk Actions
    public const string BulkApproveAllButton = "BulkApproveAllButton";
    public const string BulkRejectAllButton = "BulkRejectAllButton";

    // Loading Indicator
    public const string LoadingIndicator = "LoadingIndicator";

    #endregion

    public ApprovalWorkflowPageObject(IApp app, UITestHelpers helpers)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
    }

    #region Navigation

    /// <summary>
    /// Waits for the Approval Workflow page to be fully loaded
    /// </summary>
    public void WaitForPageLoad()
    {
        _helpers.WaitForText("Approval Workflow", timeout: TimeSpan.FromSeconds(30));
        _helpers.WaitForElement(RefreshButton);
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the pending count from statistics card
    /// </summary>
    public string GetPendingCount()
    {
        return _helpers.GetElementText(PendingCard);
    }

    /// <summary>
    /// Gets the approved count from statistics card
    /// </summary>
    public string GetApprovedCount()
    {
        return _helpers.GetElementText(ApprovedCard);
    }

    /// <summary>
    /// Gets the rejected count from statistics card
    /// </summary>
    public string GetRejectedCount()
    {
        return _helpers.GetElementText(RejectedCard);
    }

    /// <summary>
    /// Gets the total size from statistics card
    /// </summary>
    public string GetTotalSize()
    {
        return _helpers.GetElementText(TotalSizeCard);
    }

    /// <summary>
    /// Verifies all statistics cards are displayed
    /// </summary>
    public void VerifyStatisticsCardsDisplayed()
    {
        _helpers.AssertTextDisplayed("Pending");
        _helpers.AssertTextDisplayed("Approved");
        _helpers.AssertTextDisplayed("Rejected");
        _helpers.AssertTextDisplayed("Total Size");
    }

    #endregion

    #region Filters

    /// <summary>
    /// Applies channel filter
    /// </summary>
    public void ApplyChannelFilter(string channelName)
    {
        _helpers.TapElement(ChannelFilterCombo);
        _helpers.TapByText(channelName);
    }

    /// <summary>
    /// Applies priority filter
    /// </summary>
    public void ApplyPriorityFilter(int priority)
    {
        _helpers.TapElement(PriorityFilterCombo);
        _helpers.TapByText(priority.ToString());
    }

    /// <summary>
    /// Applies file size filter
    /// </summary>
    public void ApplyFileSizeFilter(long? minSize, long? maxSize)
    {
        if (minSize.HasValue)
        {
            _helpers.EnterText(MinSizeFilterInput, minSize.Value.ToString());
        }

        if (maxSize.HasValue)
        {
            _helpers.EnterText(MaxSizeFilterInput, maxSize.Value.ToString());
        }
    }

    /// <summary>
    /// Applies date range filter
    /// </summary>
    public void ApplyDateRangeFilter(DateTime? fromDate, DateTime? toDate)
    {
        if (fromDate.HasValue)
        {
            _helpers.TapElement(DateFromPicker);
            // Date picker interaction is platform-specific
            // Implementation may vary based on platform
        }

        if (toDate.HasValue)
        {
            _helpers.TapElement(DateToPicker);
        }
    }

    /// <summary>
    /// Applies extension filter
    /// </summary>
    public void ApplyExtensionFilter(string extensions)
    {
        _helpers.EnterText(ExtensionFilterInput, extensions);
    }

    /// <summary>
    /// Clears all filters
    /// </summary>
    public void ClearAllFilters()
    {
        _helpers.TapElement(ClearFiltersButton);
    }

    /// <summary>
    /// Applies multiple filters at once
    /// </summary>
    public void ApplyFilters(FilterPresetData filterData)
    {
        if (filterData.ChannelFilter != null)
        {
            ApplyChannelFilter(filterData.ChannelFilter);
        }

        if (filterData.PriorityFilter.HasValue)
        {
            ApplyPriorityFilter(filterData.PriorityFilter.Value);
        }

        if (filterData.MinSizeFilter.HasValue || filterData.MaxSizeFilter.HasValue)
        {
            ApplyFileSizeFilter(filterData.MinSizeFilter, filterData.MaxSizeFilter);
        }

        if (filterData.ExtensionFilter != null)
        {
            ApplyExtensionFilter(filterData.ExtensionFilter);
        }
    }

    #endregion

    #region Filter Presets

    /// <summary>
    /// Saves current filters as a preset
    /// </summary>
    public void SaveFilterPreset(string presetName, string description)
    {
        _helpers.TapElement(SavePresetButton);
        // Dialog interaction for saving preset
        _helpers.WaitForText("Save Filter Preset");
        _helpers.EnterText("PresetNameInput", presetName);
        _helpers.EnterText("PresetDescriptionInput", description);
        _helpers.TapByText("Save");
    }

    /// <summary>
    /// Loads a filter preset
    /// </summary>
    public void LoadFilterPreset(string presetName)
    {
        _helpers.TapElement(PresetSelector);
        _helpers.TapByText(presetName);
        _helpers.TapElement(LoadPresetButton);
    }

    /// <summary>
    /// Deletes a filter preset
    /// </summary>
    public void DeleteFilterPreset(string presetName)
    {
        _helpers.TapElement(PresetSelector);
        _helpers.TapByText(presetName);
        _helpers.TapElement(DeletePresetButton);
        // Confirm deletion
        _helpers.TapByText("Delete");
    }

    /// <summary>
    /// Sets a preset as default
    /// </summary>
    public void SetDefaultPreset(string presetName)
    {
        _helpers.TapElement(PresetSelector);
        _helpers.TapByText(presetName);
        _helpers.TapElement(SetDefaultPresetButton);
    }

    /// <summary>
    /// Verifies preset is loaded
    /// </summary>
    public void VerifyPresetLoaded(string presetName)
    {
        _helpers.AssertTextDisplayed(presetName);
    }

    #endregion

    #region Approvals List

    /// <summary>
    /// Refreshes approvals list
    /// </summary>
    public void RefreshApprovals()
    {
        _helpers.TapElement(RefreshButton);
        _helpers.WaitForLoadingComplete(LoadingIndicator);
    }

    /// <summary>
    /// Selects all approvals
    /// </summary>
    public void SelectAll()
    {
        _helpers.TapElement(SelectAllButton);
    }

    /// <summary>
    /// Clears selection
    /// </summary>
    public void ClearSelection()
    {
        _helpers.TapElement(ClearSelectionButton);
    }

    /// <summary>
    /// Approves selected items
    /// </summary>
    public void ApproveSelected()
    {
        _helpers.TapElement(ApproveSelectedButton);
        _helpers.WaitForLoadingComplete(LoadingIndicator);
    }

    /// <summary>
    /// Rejects selected items
    /// </summary>
    public void RejectSelected()
    {
        _helpers.TapElement(RejectSelectedButton);
        _helpers.WaitForLoadingComplete(LoadingIndicator);
    }

    /// <summary>
    /// Selects an approval item by file name
    /// </summary>
    public void SelectApproval(string fileName)
    {
        _helpers.TapByText(fileName);
    }

    /// <summary>
    /// Approves a specific file
    /// </summary>
    public void ApproveFile(string fileName)
    {
        SelectApproval(fileName);
        _helpers.TapByText("Approve");
    }

    /// <summary>
    /// Rejects a specific file
    /// </summary>
    public void RejectFile(string fileName)
    {
        SelectApproval(fileName);
        _helpers.TapByText("Reject");
    }

    /// <summary>
    /// Previews a file
    /// </summary>
    public void PreviewFile(string fileName)
    {
        SelectApproval(fileName);
        _helpers.TapByText("Preview");
        _helpers.WaitForElement(PreviewPanel);
    }

    /// <summary>
    /// Verifies file is displayed in list
    /// </summary>
    public void VerifyFileDisplayed(string fileName)
    {
        _helpers.AssertTextDisplayed(fileName);
    }

    /// <summary>
    /// Verifies empty state is shown
    /// </summary>
    public void VerifyEmptyState()
    {
        _helpers.AssertTextDisplayed("No approvals found");
        _helpers.AssertTextDisplayed("Try adjusting your filters or check back later");
    }

    /// <summary>
    /// Gets the number of displayed approvals from the header
    /// </summary>
    public int GetDisplayedApprovalsCount()
    {
        var headerText = _helpers.GetElementText("ApprovalsListHeader");
        // Extract count from text like "Pending Approvals (5)"
        var match = System.Text.RegularExpressions.Regex.Match(headerText, @"\((\d+)\)");
        if (match.Success)
        {
            return int.Parse(match.Groups[1].Value);
        }
        return 0;
    }

    #endregion

    #region Sorting

    /// <summary>
    /// Sorts by specified field
    /// </summary>
    public void SortBy(string sortField)
    {
        _helpers.TapElement(SortByCombo);
        _helpers.TapByText(sortField);
    }

    /// <summary>
    /// Toggles sort direction (ascending/descending)
    /// </summary>
    public void ToggleSortDirection()
    {
        _helpers.TapElement(SortDirectionToggle);
    }

    /// <summary>
    /// Applies sorting with specified field and direction
    /// </summary>
    public void ApplySorting(string sortField, bool descending)
    {
        SortBy(sortField);

        // Check current sort direction and toggle if needed
        // This is a simplified implementation
        if (descending)
        {
            ToggleSortDirection();
        }
    }

    #endregion

    #region File Preview

    /// <summary>
    /// Verifies preview panel is displayed
    /// </summary>
    public void VerifyPreviewDisplayed()
    {
        _helpers.AssertElementDisplayed(PreviewPanel);
        _helpers.AssertTextDisplayed("File Preview");
    }

    /// <summary>
    /// Verifies thumbnail is displayed
    /// </summary>
    public void VerifyThumbnailDisplayed()
    {
        _helpers.AssertElementDisplayed(ThumbnailImage);
    }

    /// <summary>
    /// Refreshes thumbnail
    /// </summary>
    public void RefreshThumbnail()
    {
        _helpers.TapElement(RefreshThumbnailButton);
    }

    /// <summary>
    /// Verifies video metadata is displayed
    /// </summary>
    public void VerifyVideoMetadataDisplayed()
    {
        _helpers.AssertElementDisplayed(VideoMetadataSection);
        _helpers.AssertTextDisplayed("Duration");
        _helpers.AssertTextDisplayed("Resolution");
        _helpers.AssertTextDisplayed("Codec");
    }

    /// <summary>
    /// Approves file from preview panel
    /// </summary>
    public void ApproveFromPreview()
    {
        _helpers.TapElement(PreviewApproveButton);
    }

    /// <summary>
    /// Rejects file from preview panel
    /// </summary>
    public void RejectFromPreview()
    {
        _helpers.TapElement(PreviewRejectButton);
    }

    #endregion

    #region Bulk Actions

    /// <summary>
    /// Bulk approves all filtered files
    /// </summary>
    public void BulkApproveAll()
    {
        _helpers.ScrollToElement(BulkApproveAllButton);
        _helpers.TapElement(BulkApproveAllButton);
        // Confirm action
        _helpers.WaitForText("Confirm Bulk Approve");
        _helpers.TapByText("Confirm");
        _helpers.WaitForLoadingComplete(LoadingIndicator);
    }

    /// <summary>
    /// Bulk rejects all filtered files
    /// </summary>
    public void BulkRejectAll()
    {
        _helpers.ScrollToElement(BulkRejectAllButton);
        _helpers.TapElement(BulkRejectAllButton);
        // Confirm action
        _helpers.WaitForText("Confirm Bulk Reject");
        _helpers.TapByText("Confirm");
        _helpers.WaitForLoadingComplete(LoadingIndicator);
    }

    #endregion

    #region Activity Log

    /// <summary>
    /// Verifies activity log is displayed
    /// </summary>
    public void VerifyActivityLogDisplayed()
    {
        _helpers.AssertTextDisplayed("Activity Log");
    }

    /// <summary>
    /// Verifies an activity message appears in the log
    /// </summary>
    public void VerifyActivityLogMessage(string expectedMessage)
    {
        _helpers.AssertTextDisplayed(expectedMessage);
    }

    /// <summary>
    /// Clears activity log
    /// </summary>
    public void ClearActivityLog()
    {
        _helpers.TapElement(ClearActivityLogButton);
    }

    /// <summary>
    /// Verifies activity log empty state
    /// </summary>
    public void VerifyActivityLogEmpty()
    {
        _helpers.AssertTextDisplayed("No activity yet");
    }

    #endregion

    #region General

    /// <summary>
    /// Waits for loading to complete
    /// </summary>
    public void WaitForLoadingComplete()
    {
        _helpers.WaitForLoadingComplete(LoadingIndicator);
    }

    /// <summary>
    /// Verifies page is fully loaded and interactive
    /// </summary>
    public void VerifyPageReady()
    {
        WaitForPageLoad();
        VerifyStatisticsCardsDisplayed();
        _helpers.AssertElementEnabled(RefreshButton);
    }

    /// <summary>
    /// Takes a screenshot of the approval workflow page
    /// </summary>
    public void CaptureScreenshot(string description)
    {
        _helpers.Screenshot($"ApprovalWorkflow_{description}");
    }

    #endregion
}
