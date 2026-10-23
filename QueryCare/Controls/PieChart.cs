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

}