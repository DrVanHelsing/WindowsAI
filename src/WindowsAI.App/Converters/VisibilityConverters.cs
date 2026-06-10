using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace WindowsAI_App.Converters;

/// <summary>
/// Static visibility helpers for x:Bind function bindings. Required inside Window
/// markup (e.g. OverlayWindow): x:Bind Converter= emits SetConverterLookupRoot(this),
/// which needs a FrameworkElement — Window is not one, so converters can't be used there.
/// </summary>
public static class Vis
{
    public static Visibility If(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility IfNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
