#nullable enable
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using IFSEngine.Model;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for PaletteSlider.axaml
/// </summary>
public partial class PaletteSlider : UserControl
{
    public static readonly StyledProperty<double> ColorIndexProperty =
        AvaloniaProperty.Register<PaletteSlider, double>(
            nameof(ColorIndex), 
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public double ColorIndex
    {
        get => GetValue(ColorIndexProperty);
        set => SetValue(ColorIndexProperty, value);
    }

    public static readonly StyledProperty<FlamePalette?> PaletteProperty =
        AvaloniaProperty.Register<PaletteSlider, FlamePalette?>(nameof(Palette));

    public FlamePalette? Palette
    {
        get => GetValue(PaletteProperty);
        set 
        {
            Console.WriteLine("Palette.set");

            SetValue(PaletteProperty, value);
        }
    }

    public static readonly StyledProperty<IBrush?> ThumbBrushProperty =
        AvaloniaProperty.Register<PaletteSlider, IBrush?>(nameof(ThumbBrush), new SolidColorBrush(Colors.Red));

    public IBrush? ThumbBrush
    {
        get => GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    static PaletteSlider()
    {
        PaletteProperty.Changed.AddClassHandler<PaletteSlider>((sender, e) =>
        {
            if (e.NewValue is FlamePalette fp)
            {
                sender.SetGradient(fp);
            }
        });

        ColorIndexProperty.Changed.AddClassHandler<PaletteSlider>((sender, e) =>
        {
            sender.UpdateThumbColor();
        });
    }

    public PaletteSlider()
    {
        InitializeComponent();
    }

    private void SetGradient(FlamePalette fp)
    {
        if (fp?.Colors is null || fp.Colors.Count == 0) return;

        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };

        int count = fp.Colors.Count;
        for (int i = 0; i < count; i++)
        {
            double offset = count > 1 ? (double)i / (count - 1) : 0.0;
            gradientBrush.GradientStops.Add(new GradientStop(
                Color.FromRgb(
                    (byte)(fp.Colors[i].X * 255),
                    (byte)(fp.Colors[i].Y * 255),
                    (byte)(fp.Colors[i].Z * 255)),
                offset));
        }

        if (this.FindNameScope()?.Find("gradientPanel") is Panel panel)
        {
            panel.Background = gradientBrush;
        }

        UpdateThumbColor();
    }

    private void UpdateThumbColor()
    {
        if (Palette is null) return;
        
        var color = Palette.GetColorLerp((float)ColorIndex);
        ThumbBrush = new SolidColorBrush(Color.FromRgb(
            (byte)(color.X * 255),
            (byte)(color.Y * 255),
            (byte)(color.Z * 255)));
    }

    private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateThumbColor();
    }
}