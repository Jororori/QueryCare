using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace QueryCare.Controls;

public sealed class PieChart : FrameworkElement
{
     public static readonly DependencyProperty GoodProperty = Register(nameof(Good));
    public static readonly DependencyProperty SlowProperty = Register(nameof(Slow));
    public static readonly DependencyProperty FrozenProperty = Register(nameof(Frozen));

    private static readonly Brush GoodBrush = Freeze(Color.FromRgb(0x22, 0xA8, 0x76));
    private static readonly Brush SlowBrush = Freeze(Color.FromRgb(0xE3, 0xA2, 0x38));
    private static readonly Brush FrozenBrush = Freeze(Color.FromRgb(0xDF, 0x52, 0x5E));
    private static readonly Brush EmptyBrush = Freeze(Color.FromRgb(0xE9, 0xEC, 0xF2));
    private static readonly Pen HaloPen = new(Freeze(Color.FromRgb(0xEC, 0xEF, 0xF4)), 1);

    public int Good { get => (int)GetValue(GoodProperty); set => SetValue(GoodProperty, value); }
    public int Slow { get => (int)GetValue(SlowProperty); set => SetValue(SlowProperty, value); }
    public int Frozen { get => (int)GetValue(FrozenProperty); set => SetValue(FrozenProperty, value); }

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(int), typeof(PieChart),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double outer = Math.Min(ActualWidth, ActualHeight) / 2;
        if (outer <= 8) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = outer - 7;

        dc.DrawEllipse(Brushes.White, HaloPen, center, outer, outer);

        int total = Good + Slow + Frozen;
        if (total == 0)
        {
            dc.DrawEllipse(EmptyBrush, null, center, r, r);
            return;
        }

        double start = -90;
        foreach (var (value, brush) in new[] { (Frozen, FrozenBrush), (Slow, SlowBrush), (Good, GoodBrush) })
        {
            if (value == 0) continue;
            double sweep = 360.0 * value / total;
            if (sweep >= 359.99) dc.DrawEllipse(brush, null, center, r, r);
            else dc.DrawGeometry(brush, null, Slice(center, r, start, sweep));
            start += sweep;
        }
    }

}