using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace QueryCare;

public sealed class LevelBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, (Brush Bg, Brush Fg)> Map = BuildMap();

    private static Dictionary<string, (Brush, Brush)> BuildMap()
    {
        var red = (B("#FDECEE"), B("#D63C4A"));
        var amber = (B("#FEF4E4"), B("#C47A12"));
        var green = (B("#E6F6EF"), B("#178A5E"));
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["Crítico"] = red,
            ["CRÍTICA"] = red,
            ["Alto"] = red,
            ["ALTA"] = red,
            ["Congelado"] = red,
            ["Error"] = red,
            ["Medio"] = amber,
            ["MEDIA"] = amber,
            ["Lento"] = amber,
            ["Bajo"] = green,
            ["Bien"] = green
        };
    }

    private static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var pair = Map.TryGetValue(value as string ?? "", out var p) ? p : (B("#EEF1F5"), B("#5B6475"));
        return (parameter as string) == "bg" ? pair.Item1 : pair.Item2;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible cuando el número es mayor que cero (o, con parámetro "invert", cuando es cero).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool positive = value is int n && n > 0;
        if ((parameter as string) == "invert") positive = !positive;
        return positive ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

