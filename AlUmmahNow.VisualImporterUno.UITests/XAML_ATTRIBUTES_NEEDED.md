# Required x:Name Attributes for UI Testing

This document lists the x:Name attributes that should be added to XAML files to enable proper UI test automation.

## Overview

For UI tests to reliably interact with elements, critical UI components need `x:Name` or `AutomationProperties.AutomationId` attributes. This document maps Page Object constants to the XAML elements that need these attributes.

## WatchManagementPage.xaml

### Action Buttons (Lines 143-165)
```xml
<!-- Line 144 -->
<Button x:Name="CreateWatchButton"
        Style="{StaticResource PrimaryButtonStyle}"
        Command="{x:Bind ViewModel.OpenCreateWatchDialogCommand}"
        MinWidth="160">

<!-- Line 152 -->
<Button x:Name="BatchCreateButton"
        Content="Batch Create"
        Background="#7C3AED"
        .../>

<!-- Line 161 -->
<Button x:Name="RefreshWatchesButton"
        Content="Refresh"
        Style="{StaticResource SecondaryButtonStyle}"
        .../>
```

### Statistics Cards TextBlocks (Lines 187-241)
```xml
<!-- Line 197 - Total Watches Count -->
<TextBlock x:Name="TotalWatchesCount"
           Text="{x:Bind ViewModel.TotalWatches, Mode=OneWay}"
           Style="{StaticResource StatNumberStyle}"/>

<!-- Line 211 - Active Watches Count -->
<TextBlock x:Name="ActiveWatchesCount"
           Text="{x:Bind ViewModel.ActiveWatchesCount, Mode=OneWay}"
           Style="{StaticResource StatNumberStyle}"/>

<!-- Line 225 - Files Detected Count -->
<TextBlock x:Name="FilesDetectedCount"
           Text="{x:Bind ViewModel.TotalFilesDetected, Mode=OneWay}"
           Style="{StaticResource StatNumberStyle}"/>

<!-- Line 239 - Pending Approvals Count -->
<TextBlock x:Name="PendingApprovalsCount"
           Text="{x:Bind ViewModel.PendingApprovalsCount, Mode=OneWay}"
           Style="{StaticResource StatNumberStyle}"/>
```

### Active Watches ListView (Line 254)
```xml
<ListView x:Name="ActiveWatchesList"
          ItemsSource="{x:Bind ViewModel.ActiveWatches, Mode=OneWay}"
          .../>
```

### Create Watch Dialog Inputs (Lines 502-560)
```xml
<!-- Line 502 - Channel Name Input -->
<TextBox x:Name="ChannelNameInput"
         Text="{x:Bind ViewModel.NewWatchChannelName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
         .../>

<!-- Line 505 - Channel Name Validation -->
<TextBlock x:Name="ChannelNameValidation"
           Text="{x:Bind ViewModel.ChannelNameValidationMessage, Mode=OneWay}"
           .../>

<!-- Line 520 - Watch Path Input -->
<TextBox x:Name="WatchPathInput"
         Text="{x:Bind ViewModel.NewWatchPath, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
         .../>

<!-- Line 529 - Watch Path Validation -->
<TextBlock x:Name="WatchPathValidation"
           Text="{x:Bind ViewModel.WatchPathValidationMessage, Mode=OneWay}"
           .../>

<!-- Line 538 - Processing Delay Slider -->
<Slider x:Name="ProcessingDelaySlider"
        Value="{x:Bind ViewModel.ProcessingDelaySeconds, Mode=TwoWay}"
        .../>

<!-- Line 552 - Include Subdirectories Checkbox -->
<CheckBox x:Name="IncludeSubdirectoriesCheckbox"
          Content="Include subdirectories"
          IsChecked="{x:Bind ViewModel.IncludeSubdirectories, Mode=TwoWay}"
          .../>

<!-- Line 560 - Extensions Input -->
<TextBox x:Name="ExtensionsInput"
         Text="{x:Bind ViewModel.SelectedExtensions, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
         .../>
```

### Batch Create Dialog Elements (Lines 614-650)
```xml
<!-- Line 614 - Template Selector -->
<ComboBox x:Name="TemplateSelector"
          ItemsSource="{x:Bind ViewModel.SavedTemplates, Mode=OneWay}"
          .../>

<!-- Line 634 - Number of Watches Input -->
<NumberBox x:Name="NumberOfWatchesInput"
           Value="{x:Bind ViewModel.NumberOfWatches, Mode=TwoWay}"
           .../>

<!-- Line 642 - Starting Number Input -->
<NumberBox x:Name="StartingNumberInput"
           Value="{x:Bind ViewModel.StartingNumber, Mode=TwoWay}"
           .../>

<!-- Line 650 - Batch Base Path Input -->
<TextBox x:Name="BatchBasePathInput"
         Text="{x:Bind ViewModel.BatchBasePath, Mode=TwoWay}"
         .../>

<!-- Line 656 - Generate Preview Button -->
<Button x:Name="GeneratePreviewButton"
        Content="Generate Preview"
        .../>

<!-- Line 667 - Preview List -->
<ItemsRepeater x:Name="PreviewList"
               ItemsSource="{x:Bind ViewModel.PreviewWatches, Mode=OneWay}">
               .../>

<!-- Line 699 - Batch Progress Bar -->
<ProgressBar x:Name="BatchProgressBar"
             Value="{x:Bind ViewModel.BatchCreateProgress, Mode=OneWay}"
             .../>
```

### Pending Approvals Section (Lines 367-396)
```xml
<!-- Line 367 - Refresh Approvals Button -->
<Button x:Name="RefreshApprovalsButton"
        Content="Refresh"
        .../>

<!-- Line 372 - Approve Selected Button -->
<Button x:Name="ApproveSelectedButton"
        Content="Approve Selected"
        .../>

<!-- Line 378 - Reject Selected Button -->
<Button x:Name="RejectSelectedButton"
        Content="Reject Selected"
        .../>

<!-- Line 392 - Pending Approvals List -->
<ListView x:Name="PendingApprovalsList"
          ItemsSource="{x:Bind ViewModel.FilteredPendingApprovals, Mode=OneWay}"
          .../>
```

### Loading Overlay (Line 795)
```xml
<Border x:Name="LoadingOverlay"
        Grid.RowSpan="2"
        Background="#80000000"
        .../>
```

## ApprovalWorkflowPage.xaml

### Header Action Buttons
```xml
<Button x:Name="RefreshApprovalsButton"
        .../>

<Button x:Name="SelectAllButton"
        Content="Select All"
        .../>

<Button x:Name="ClearSelectionButton"
        Content="Clear Selection"
        .../>

<Button x:Name="ApproveSelectedButton"
        Style="{StaticResource PrimaryButtonStyle}"
        Command="{x:Bind ViewModel.ApproveSelectedCommand}"
        .../>

<Button x:Name="RejectSelectedButton"
        Style="{StaticResource DangerButtonStyle}"
        Command="{x:Bind ViewModel.RejectSelectedCommand}"
        .../>
```

### Statistics Cards
```xml
<Border x:Name="PendingStatCard" ...>
<Border x:Name="ApprovedStatCard" ...>
<Border x:Name="RejectedStatCard" ...>
<Border x:Name="TotalSizeStatCard" ...>
```

### Filter Section
```xml
<StackPanel x:Name="FiltersSection" ...>

<Button x:Name="ClearFiltersButton"
        Content="Clear"
        .../>

<ComboBox x:Name="ChannelFilterCombo"
          ItemsSource="{x:Bind ViewModel.AvailableChannels, Mode=OneWay}"
          .../>

<ComboBox x:Name="PriorityFilterCombo"
          SelectedItem="{x:Bind ViewModel.PriorityFilter, Mode=TwoWay}"
          .../>

<TextBox x:Name="MinSizeFilterInput"
         Text="{x:Bind ViewModel.MinSizeFilter, Mode=TwoWay, Converter={StaticResource StringToLongConverter}}"
         .../>

<TextBox x:Name="MaxSizeFilterInput"
         Text="{x:Bind ViewModel.MaxSizeFilter, Mode=TwoWay, Converter={StaticResource StringToLongConverter}}"
         .../>

<DatePicker x:Name="DateFromPicker"
            Date="{x:Bind ViewModel.DateFromFilter, Mode=TwoWay, Converter={StaticResource DateTimeToDateConverter}}"
            .../>

<DatePicker x:Name="DateToPicker"
            Date="{x:Bind ViewModel.DateToFilter, Mode=TwoWay, Converter={StaticResource DateTimeToDateConverter}}"
            .../>

<TextBox x:Name="ExtensionFilterInput"
         Text="{x:Bind ViewModel.ExtensionFilter, Mode=TwoWay}"
         .../>
```

### Filter Presets
```xml
<ComboBox x:Name="PresetSelector"
          ItemsSource="{x:Bind ViewModel.SavedPresets, Mode=OneWay}"
          .../>

<Button x:Name="LoadPresetButton"
        Content="Load"
        .../>

<Button x:Name="SavePresetButton"
        Content="Save Current"
        .../>

<Button x:Name="DeletePresetButton"
        Command="{x:Bind ViewModel.DeletePresetCommand}"
        .../>

<Button x:Name="SetDefaultPresetButton"
        Command="{x:Bind ViewModel.SetDefaultPresetCommand}"
        .../>
```

### Approvals List and Sorting
```xml
<ListView x:Name="ApprovalsList"
          ItemsSource="{x:Bind ViewModel.FilteredApprovals, Mode=OneWay}"
          .../>

<ComboBox x:Name="SortByCombo"
          SelectedItem="{x:Bind ViewModel.SortBy, Mode=TwoWay}"
          .../>

<ToggleButton x:Name="SortDirectionToggle"
              IsChecked="{x:Bind ViewModel.SortDescending, Mode=TwoWay}"
              .../>

<ProgressBar x:Name="LoadingIndicator"
             IsIndeterminate="True"
             .../>
```

### File Preview Panel
```xml
<Border x:Name="PreviewPanel" ...>

<Image x:Name="ThumbnailImage" ...>

<Button x:Name="RefreshThumbnailButton"
        Content="Refresh"
        .../>

<StackPanel x:Name="VideoMetadataSection" ...>

<Button x:Name="PreviewApproveButton"
        Content="Approve"
        .../>

<Button x:Name="PreviewRejectButton"
        Content="Reject"
        .../>
```

### Bulk Actions
```xml
<Button x:Name="BulkApproveAllButton"
        Content="Bulk Approve All"
        .../>

<Button x:Name="BulkRejectAllButton"
        Content="Bulk Reject All"
        .../>
```

### Activity Log
```xml
<ItemsControl x:Name="ActivityLog"
              ItemsSource="{x:Bind ViewModel.ActivityLog, Mode=OneWay}"
              .../>

<Button x:Name="ClearActivityLogButton"
        Content="Clear"
        .../>
```

## MainWindow.xaml / Dashboard

### Navigation Menu Items
```xml
<NavigationView x:Name="NavigationView" ...>

<NavigationViewItem x:Name="DashboardMenuItem"
                    Content="Dashboard"
                    .../>

<NavigationViewItem x:Name="WatchManagementMenuItem"
                    Content="Watch Management"
                    .../>

<NavigationViewItem x:Name="ApprovalWorkflowMenuItem"
                    Content="Approval Workflow"
                    .../>

<NavigationViewItem x:Name="FileDiscoveryMenuItem"
                    Content="File Discovery"
                    .../>

<NavigationViewItem x:Name="ImportMenuItem"
                    Content="Import"
                    .../>

<NavigationViewItem x:Name="AnalyticsMenuItem"
                    Content="Analytics"
                    .../>

<NavigationViewItem x:Name="ServiceStatusMenuItem"
                    Content="Service Status"
                    .../>

<NavigationViewItem x:Name="SettingsMenuItem"
                    Content="Settings"
                    .../>
```

### App Bar
```xml
<TextBlock x:Name="AppTitle"
           Text="Al Ummah Now Visual Importer"
           .../>

<Button x:Name="UserMenu" ...>

<Button x:Name="NotificationBell" ...>

<TextBox x:Name="SearchBox" ...>

<Button x:Name="MenuToggleButton" ...>
```

## Implementation Priority

### High Priority (Required for basic tests)
1. All navigation menu items
2. Primary action buttons (Create, Refresh, Save)
3. Form inputs (TextBox, ComboBox, CheckBox)
4. Statistics/count displays
5. List/ListView elements

### Medium Priority (Required for comprehensive tests)
1. Dialog elements
2. Filter controls
3. Sort controls
4. Preview panel elements
5. Validation message displays

### Low Priority (Nice to have)
1. Empty state containers
2. Loading indicators
3. Progress bars
4. Icon/decoration elements

## Alternative: AutomationProperties

If `x:Name` cannot be added (e.g., for DataTemplate items), use `AutomationProperties.AutomationId`:

```xml
<Button AutomationProperties.AutomationId="ApproveButton"
        AutomationProperties.Name="Approve File"
        Content="Approve" />
```

## Testing Verification

After adding x:Name attributes, verify they're accessible in tests:

```csharp
// Test that element can be found
Helpers.WaitForElement("CreateWatchButton");
Helpers.AssertElementDisplayed("CreateWatchButton");
```

## Notes

- All element identifiers should match the constants defined in Page Object classes
- Use descriptive names that clearly indicate the element's purpose
- Maintain consistency across pages (e.g., all "Refresh" buttons should have similar naming)
- Document any platform-specific differences or limitations
