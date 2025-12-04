using NUnit.Framework;
using Uno.UITest;
using AlUmmahNow.VisualImporterUno.UITests.Helpers;

namespace AlUmmahNow.VisualImporterUno.UITests.PageObjects;

/// <summary>
/// Page Object Model for Watch Management Page.
/// Encapsulates all UI interactions and element identifiers for the Watch Management feature.
/// </summary>
public class WatchManagementPageObject
{
    private readonly IApp _app;
    private readonly UITestHelpers _helpers;

    #region Element Identifiers

    // Page Elements
    public const string PageTitle = "WatchManagementPageTitle";
    public const string StatusMessage = "WatchManagementStatusMessage";

    // Action Buttons
    public const string CreateWatchButton = "CreateWatchButton";
    public const string BatchCreateButton = "BatchCreateButton";
    public const string RefreshButton = "RefreshWatchesButton";

    // Statistics Cards
    public const string TotalWatchesCard = "TotalWatchesCard";
    public const string ActiveWatchesCard = "ActiveWatchesCard";
    public const string FilesDetectedCard = "FilesDetectedCard";
    public const string PendingApprovalsCard = "PendingApprovalsCard";

    public const string TotalWatchesCount = "TotalWatchesCount";
    public const string ActiveWatchesCount = "ActiveWatchesCount";
    public const string FilesDetectedCount = "FilesDetectedCount";
    public const string PendingApprovalsCount = "PendingApprovalsCount";

    // Active Watches List
    public const string ActiveWatchesList = "ActiveWatchesList";
    public const string ActiveWatchesEmptyState = "ActiveWatchesEmptyState";

    // Create Watch Dialog
    public const string CreateWatchDialog = "CreateWatchDialog";
    public const string ChannelNameInput = "ChannelNameInput";
    public const string WatchPathInput = "WatchPathInput";
    public const string BrowsePathButton = "BrowsePathButton";
    public const string ProcessingDelaySlider = "ProcessingDelaySlider";
    public const string IncludeSubdirectoriesCheckbox = "IncludeSubdirectoriesCheckbox";
    public const string ExtensionsInput = "ExtensionsInput";
    public const string CreateWatchConfirmButton = "CreateWatchConfirmButton";
    public const string CreateWatchCancelButton = "CreateWatchCancelButton";
    public const string CreateWatchErrorMessage = "CreateWatchErrorMessage";
    public const string CreateWatchLoadingIndicator = "CreateWatchLoadingIndicator";

    // Validation Messages
    public const string ChannelNameValidation = "ChannelNameValidation";
    public const string WatchPathValidation = "WatchPathValidation";

    // Batch Create Dialog
    public const string BatchCreateDialog = "BatchCreateDialog";
    public const string BatchTemplateTab = "BatchTemplateTab";
    public const string BatchImportTab = "BatchImportTab";
    public const string TemplateSelector = "TemplateSelector";
    public const string NumberOfWatchesInput = "NumberOfWatchesInput";
    public const string StartingNumberInput = "StartingNumberInput";
    public const string BatchBasePathInput = "BatchBasePathInput";
    public const string GeneratePreviewButton = "GeneratePreviewButton";
    public const string PreviewList = "PreviewList";
    public const string CreateAllWatchesButton = "CreateAllWatchesButton";
    public const string BatchProgressBar = "BatchProgressBar";

    // Pending Approvals List
    public const string PendingApprovalsList = "PendingApprovalsList";
    public const string PendingApprovalsEmptyState = "PendingApprovalsEmptyState";
    public const string ApproveSelectedButton = "ApproveSelectedButton";
    public const string RejectSelectedButton = "RejectSelectedButton";
    public const string RefreshApprovalsButton = "RefreshApprovalsButton";

    // Loading Overlay
    public const string LoadingOverlay = "LoadingOverlay";

    #endregion

    public WatchManagementPageObject(IApp app, UITestHelpers helpers)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
    }

    #region Navigation

    /// <summary>
    /// Waits for the Watch Management page to be fully loaded
    /// </summary>
    public void WaitForPageLoad()
    {
        _helpers.WaitForText("Watch Management", timeout: TimeSpan.FromSeconds(30));
        _helpers.WaitForElement(CreateWatchButton);
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the total watches count from statistics card
    /// </summary>
    public string GetTotalWatchesCount()
    {
        return _helpers.GetElementText(TotalWatchesCount);
    }

    /// <summary>
    /// Gets the active watches count from statistics card
    /// </summary>
    public string GetActiveWatchesCount()
    {
        return _helpers.GetElementText(ActiveWatchesCount);
    }

    /// <summary>
    /// Gets the files detected count from statistics card
    /// </summary>
    public string GetFilesDetectedCount()
    {
        return _helpers.GetElementText(FilesDetectedCount);
    }

    /// <summary>
    /// Gets the pending approvals count from statistics card
    /// </summary>
    public string GetPendingApprovalsCount()
    {
        return _helpers.GetElementText(PendingApprovalsCount);
    }

    /// <summary>
    /// Verifies all statistics cards are displayed
    /// </summary>
    public void VerifyStatisticsCardsDisplayed()
    {
        _helpers.AssertTextDisplayed("Total Watches");
        _helpers.AssertTextDisplayed("Active Watches");
        _helpers.AssertTextDisplayed("Files Detected");
        _helpers.AssertTextDisplayed("Pending Approvals");
    }

    #endregion

    #region Create Watch

    /// <summary>
    /// Opens the Create Watch dialog
    /// </summary>
    public void OpenCreateWatchDialog()
    {
        _helpers.TapElement(CreateWatchButton);
        _helpers.WaitForText("Create New Watch");
    }

    /// <summary>
    /// Fills the create watch form with provided data
    /// </summary>
    public void FillCreateWatchForm(WatchData watchData)
    {
        _helpers.EnterText(ChannelNameInput, watchData.ChannelName);
        _helpers.EnterText(WatchPathInput, watchData.WatchPath);

        // Set processing delay using slider (approximate value)
        // Note: Slider interaction may need platform-specific implementation

        if (!watchData.IncludeSubdirectories)
        {
            _helpers.TapElement(IncludeSubdirectoriesCheckbox);
        }

        _helpers.EnterText(ExtensionsInput, watchData.Extensions);
    }

    /// <summary>
    /// Submits the create watch form
    /// </summary>
    public void SubmitCreateWatch()
    {
        _helpers.TapElement(CreateWatchConfirmButton);
    }

    /// <summary>
    /// Cancels the create watch dialog
    /// </summary>
    public void CancelCreateWatch()
    {
        _helpers.TapElement(CreateWatchCancelButton);
    }

    /// <summary>
    /// Creates a watch with the full workflow (open, fill, submit)
    /// </summary>
    public void CreateWatch(WatchData watchData)
    {
        OpenCreateWatchDialog();
        FillCreateWatchForm(watchData);
        SubmitCreateWatch();
        WaitForDialogClose();
    }

    /// <summary>
    /// Verifies that validation error is displayed for a field
    /// </summary>
    public void VerifyValidationError(string fieldValidationId)
    {
        _helpers.AssertElementDisplayed(fieldValidationId);
    }

    /// <summary>
    /// Verifies channel name validation message
    /// </summary>
    public void VerifyChannelNameValidation(string expectedMessage)
    {
        var actualMessage = _helpers.GetElementText(ChannelNameValidation);
        if (!actualMessage.Contains(expectedMessage))
        {
            Assert.Fail($"Expected validation message to contain '{expectedMessage}', but got '{actualMessage}'");
        }
    }

    /// <summary>
    /// Waits for create watch dialog to close
    /// </summary>
    public void WaitForDialogClose()
    {
        _helpers.WaitForElementToDisappear(CreateWatchDialog, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Verifies create watch success (dialog closed, watch appears in list)
    /// </summary>
    public void VerifyCreateWatchSuccess(string channelName)
    {
        _helpers.AssertTextDisplayed(channelName);
    }

    #endregion

    #region Batch Create

    /// <summary>
    /// Opens the Batch Create dialog
    /// </summary>
    public void OpenBatchCreateDialog()
    {
        _helpers.TapElement(BatchCreateButton);
        _helpers.WaitForText("Batch Create Watches");
    }

    /// <summary>
    /// Selects template tab in batch create dialog
    /// </summary>
    public void SelectTemplateTab()
    {
        _helpers.TapElement(BatchTemplateTab);
    }

    /// <summary>
    /// Selects import tab in batch create dialog
    /// </summary>
    public void SelectImportTab()
    {
        _helpers.TapElement(BatchImportTab);
    }

    /// <summary>
    /// Configures batch creation from template
    /// </summary>
    public void ConfigureBatchFromTemplate(int numberOfWatches, int startingNumber, string basePath)
    {
        _helpers.EnterText(NumberOfWatchesInput, numberOfWatches.ToString());
        _helpers.EnterText(StartingNumberInput, startingNumber.ToString());
        _helpers.EnterText(BatchBasePathInput, basePath);
    }

    /// <summary>
    /// Generates preview for batch creation
    /// </summary>
    public void GenerateBatchPreview()
    {
        _helpers.TapElement(GeneratePreviewButton);
        _helpers.WaitForElement(PreviewList);
    }

    /// <summary>
    /// Creates all watches from batch
    /// </summary>
    public void CreateAllBatchWatches()
    {
        _helpers.TapElement(CreateAllWatchesButton);
        _helpers.WaitForElement(BatchProgressBar);
    }

    /// <summary>
    /// Waits for batch creation to complete
    /// </summary>
    public void WaitForBatchCreationComplete()
    {
        _helpers.WaitForElementToDisappear(BatchProgressBar, TimeSpan.FromMinutes(2));
    }

    #endregion

    #region Active Watches

    /// <summary>
    /// Refreshes the watches list
    /// </summary>
    public void RefreshWatches()
    {
        _helpers.TapElement(RefreshButton);
    }

    /// <summary>
    /// Verifies a watch is displayed in the active watches list
    /// </summary>
    public void VerifyWatchDisplayed(string channelName)
    {
        _helpers.AssertTextDisplayed(channelName);
    }

    /// <summary>
    /// Verifies empty state is shown when no watches exist
    /// </summary>
    public void VerifyEmptyStateDisplayed()
    {
        _helpers.AssertTextDisplayed("No active watches");
        _helpers.AssertTextDisplayed("Create a new watch to start monitoring directories");
    }

    /// <summary>
    /// Clicks on a watch in the list
    /// </summary>
    public void SelectWatch(string channelName)
    {
        _helpers.TapByText(channelName);
    }

    /// <summary>
    /// Stops a running watch
    /// </summary>
    public void StopWatch(string channelName)
    {
        // First select the watch, then tap stop button
        SelectWatch(channelName);
        _helpers.TapByText("Stop");
    }

    /// <summary>
    /// Views watch details
    /// </summary>
    public void ViewWatchDetails(string channelName)
    {
        SelectWatch(channelName);
        _helpers.TapByText("View Details");
    }

    #endregion

    #region Pending Approvals

    /// <summary>
    /// Refreshes pending approvals list
    /// </summary>
    public void RefreshPendingApprovals()
    {
        _helpers.TapElement(RefreshApprovalsButton);
    }

    /// <summary>
    /// Approves selected items
    /// </summary>
    public void ApproveSelectedItems()
    {
        _helpers.TapElement(ApproveSelectedButton);
    }

    /// <summary>
    /// Rejects selected items
    /// </summary>
    public void RejectSelectedItems()
    {
        _helpers.TapElement(RejectSelectedButton);
    }

    /// <summary>
    /// Verifies pending approvals list is displayed
    /// </summary>
    public void VerifyPendingApprovalsDisplayed()
    {
        _helpers.AssertTextDisplayed("Pending Approvals");
    }

    /// <summary>
    /// Verifies pending approvals count in section header
    /// </summary>
    public void VerifyPendingApprovalsCount(int expectedCount)
    {
        _helpers.AssertTextDisplayed($"({expectedCount})");
    }

    #endregion

    #region General

    /// <summary>
    /// Waits for loading overlay to disappear
    /// </summary>
    public void WaitForLoadingComplete()
    {
        _helpers.WaitForLoadingComplete(LoadingOverlay);
    }

    /// <summary>
    /// Verifies page is fully loaded and interactive
    /// </summary>
    public void VerifyPageReady()
    {
        WaitForPageLoad();
        VerifyStatisticsCardsDisplayed();
        _helpers.AssertElementEnabled(CreateWatchButton);
    }

    #endregion
}
