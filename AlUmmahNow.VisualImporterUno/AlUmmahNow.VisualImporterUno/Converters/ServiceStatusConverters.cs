using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using Windows.UI;
using AlUmmahNow.VisualImporterUno.ViewModels;

namespace AlUmmahNow.VisualImporterUno.Converters;

/// <summary>
/// Converts a hex color string to a Color
/// </summary>
public class ColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hexColor && hexColor.StartsWith("#"))
        {
            try
            {
                hexColor = hexColor.TrimStart('#');
                byte a = 255;
                byte r = 0;
                byte g = 0;
                byte b = 0;

                if (hexColor.Length == 8)
                {
                    a = System.Convert.ToByte(hexColor.Substring(0, 2), 16);
                    r = System.Convert.ToByte(hexColor.Substring(2, 2), 16);
                    g = System.Convert.ToByte(hexColor.Substring(4, 2), 16);
                    b = System.Convert.ToByte(hexColor.Substring(6, 2), 16);
                }
                else if (hexColor.Length == 6)
                {
                    r = System.Convert.ToByte(hexColor.Substring(0, 2), 16);
                    g = System.Convert.ToByte(hexColor.Substring(2, 2), 16);
                    b = System.Convert.ToByte(hexColor.Substring(4, 2), 16);
                }

                return Color.FromArgb(a, r, g, b);
            }
            catch
            {
                return Color.FromArgb(255, 128, 128, 128); // Gray
            }
        }
        return Color.FromArgb(255, 128, 128, 128); // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts MessageType to background color
/// </summary>
public class MessageTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is MessageType messageType)
        {
            return messageType switch
            {
                MessageType.Error => Color.FromArgb(40, 255, 107, 107),     // Light red background
                MessageType.Warning => Color.FromArgb(40, 255, 212, 59),   // Light yellow background
                MessageType.Success => Color.FromArgb(40, 81, 207, 102),   // Light green background
                MessageType.Information => Color.FromArgb(20, 100, 149, 237), // Light blue background
                _ => Color.FromArgb(20, 128, 128, 128)                     // Light gray background
            };
        }
        
        return Color.FromArgb(20, 128, 128, 128);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts MessageType to border color
/// </summary>
public class MessageTypeToBorderColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is MessageType messageType)
        {
            return messageType switch
            {
                MessageType.Error => Color.FromArgb(255, 255, 107, 107),     // Red border
                MessageType.Warning => Color.FromArgb(255, 255, 212, 59),   // Yellow border
                MessageType.Success => Color.FromArgb(255, 81, 207, 102),   // Green border
                MessageType.Information => Color.FromArgb(255, 100, 149, 237), // Blue border
                _ => Color.FromArgb(255, 128, 128, 128)                     // Gray border
            };
        }
        
        return Color.FromArgb(255, 128, 128, 128);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// Converts inverse boolean to visibility
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts count to boolean (true if count > 0) or visibility based on parameter
/// </summary>
public class CountToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            if (parameter is string paramStr)
            {
                // Handle special parameters
                if (paramStr.Equals("Zero", StringComparison.OrdinalIgnoreCase))
                {
                    // Return visibility for zero count
                    if (targetType == typeof(Visibility))
                        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    // Return boolean for zero count
                    return count == 0;
                }

                // Try to parse as number for comparison
                if (int.TryParse(paramStr, out int compareValue))
                {
                    if (targetType == typeof(Visibility))
                        return count == compareValue ? Visibility.Visible : Visibility.Collapsed;
                    return count == compareValue;
                }
            }

            // Default behavior - true if count > 0
            if (targetType == typeof(Visibility))
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            return count > 0;
        }

        // Handle string values (like for CurrentFile)
        if (value is string stringValue)
        {
            var hasValue = !string.IsNullOrEmpty(stringValue);
            if (targetType == typeof(Visibility))
                return hasValue ? Visibility.Visible : Visibility.Collapsed;
            return hasValue;
        }

        // Default fallback
        if (targetType == typeof(Visibility))
            return Visibility.Collapsed;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts count to visibility with comparison support
/// </summary>
public class CountComparisonToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count && parameter is string paramStr)
        {
            if (int.TryParse(paramStr, out int compareValue))
            {
                return count == compareValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts service status to visibility based on parameter
/// </summary>
public class ServiceStatusToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Services.ServiceStatus status && parameter is string paramStr)
        {
            var validStatuses = paramStr.Split(',');
            foreach (var validStatus in validStatuses)
            {
                if (Enum.TryParse<Services.ServiceStatus>(validStatus.Trim(), out var compareStatus))
                {
                    if (status == compareStatus)
                    {
                        return Visibility.Visible;
                    }
                }
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts items processed count to display string
/// </summary>
public class ItemsProcessedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return $"{count:N0} items processed";
        }
        return "0 items processed";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
