using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using Windows.UI;

namespace AlUmmahNow.VisualImporter.Converters;

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
/// Converts count to boolean (true if count > 0)
/// </summary>
public class CountToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count > 0;
        }
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
