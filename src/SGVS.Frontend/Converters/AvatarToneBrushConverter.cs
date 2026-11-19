using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using SGVS.Frontend.Models;

namespace SGVS.Frontend.Converters;

// AvatarTone.Teal -> the SgvsToneTealBrush resource, so the colors stay in Styles/Resources.axaml
public sealed class AvatarToneBrushConverter : IValueConverter
{
    public static readonly AvatarToneBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AvatarTone tone || Application.Current is not { } app) return null;
        return app.TryGetResource($"SgvsTone{tone}Brush", app.ActualThemeVariant, out var brush) ? brush : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
