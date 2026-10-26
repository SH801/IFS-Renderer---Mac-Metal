#nullable enable
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IFSEngine.Model;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for PaletteButton.axaml
/// </summary>
public partial class PaletteButton : Button
{
    public static readonly StyledProperty<FlamePalette?> PaletteProperty =
        AvaloniaProperty.Register<PaletteButton, FlamePalette?>(nameof(Palette));

    public FlamePalette? Palette
    {
        get => GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    static PaletteButton()
    {
        PaletteProperty.Changed.AddClassHandler<PaletteButton>((sender, e) =>
        {
            if (e.NewValue is FlamePalette fp)
            {
                sender.SetGradient(fp);
            }
        });
    }

    public PaletteButton()
    {
        InitializeComponent();
    }

    private void SetGradient(FlamePalette fp)
    {
        if (Background is LinearGradientBrush brush)
        {
            brush.GradientStops.Clear();
            for (int i = 0; i < fp.Colors.Count; i++)
            {
                brush.GradientStops.Add(new GradientStop(
                    Color.FromRgb(
                        (byte)(fp.Colors[i].X * 255),
                        (byte)(fp.Colors[i].Y * 255),
                        (byte)(fp.Colors[i].Z * 255)),
                    i / (double)fp.Colors.Count));
            }
        }
    }
}