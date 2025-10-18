using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Globalization;

namespace AlUmmahNow.VisualImporterUno.Views;

/// <summary>
/// Approval Workflow page for managing file approvals with real-time updates
/// </summary>
public sealed partial class ApprovalWorkflowPage : Page
{
    public ApprovalViewModel ViewModel { get; }

    public ApprovalWorkflowPage()
    {
        this.InitializeComponent();

        // Get ViewModel from DI container (registered as singleton)
        ViewModel = App.GetService<ApprovalViewModel>();

        // Set DataContext for binding
        this.DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Additional initialization can be done here if needed
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Cleanup if needed
    }

    private async void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Save Filter Preset",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (sender as FrameworkElement)?.XamlRoot ?? this.XamlRoot
        };

        var stackPanel = new StackPanel { Spacing = 12 };

        var nameBox = new TextBox
        {
            Header = "Preset Name",
            PlaceholderText = "Enter preset name...",
            Text = ViewModel.NewPresetName
        };

        var descriptionBox = new TextBox
        {
            Header = "Description (Optional)",
            PlaceholderText = "Enter description...",
            Text = ViewModel.NewPresetDescription,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80
        };

        var defaultCheckBox = new CheckBox
        {
            Content = "Set as default preset",
            IsChecked = ViewModel.SetAsDefault
        };

        stackPanel.Children.Add(nameBox);
        stackPanel.Children.Add(descriptionBox);
        stackPanel.Children.Add(defaultCheckBox);

        dialog.Content = stackPanel;

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            ViewModel.NewPresetName = nameBox.Text;
            ViewModel.NewPresetDescription = descriptionBox.Text;
            ViewModel.SetAsDefault = defaultCheckBox.IsChecked ?? false;

            await ViewModel.SavePresetCommand.ExecuteAsync(nameBox.Text);
        }
    }
}

#region Value Converters

/// <summary>
/// Converts DateTime to relative time string (e.g., "2 hours ago")
/// </summary>
public class RelativeDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "Just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} min ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hr ago";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} days ago";
            if (timeSpan.TotalDays < 30)
                return $"{(int)(timeSpan.TotalDays / 7)} weeks ago";

            return dateTime.ToString("MMM dd, yyyy");
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts priority level to background color
/// </summary>
public class PriorityColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int priority)
        {
            return priority switch
            {
                1 => "#DC2626", // High priority - Red
                2 => "#F59E0B", // Medium-high - Orange
                3 => "#3B82F6", // Medium - Blue
                4 => "#10B981", // Medium-low - Green
                5 => "#6B7280", // Low - Gray
                _ => "#6B7280"
            };
        }

        return "#6B7280";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null to Visibility (Visible if not null)
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts string to long for file size input
/// </summary>
public class StringToLongConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is long longValue)
        {
            return longValue.ToString();
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is string str && long.TryParse(str, out long result))
        {
            return result;
        }

        return null;
    }
}

/// <summary>
/// Converts DateTime to DateTimeOffset for DatePicker
/// </summary>
public class DateTimeToDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dateTime)
        {
            return new DateTimeOffset(dateTime);
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.DateTime;
        }

        return null;
    }
}

/// <summary>
/// Converts selected count to approve button text
/// </summary>
public class ApproveButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count > 0 ? $"Approve ({count})" : "Approve";
        }

        return "Approve";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts selected count to reject button text
/// </summary>
public class RejectButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count > 0 ? $"Reject ({count})" : "Reject";
        }

        return "Reject";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts selected count to status text
/// </summary>
public class SelectedCountTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count > 0 ? $"{count} items selected" : "No items selected";
        }

        return "No items selected";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

#endregion
