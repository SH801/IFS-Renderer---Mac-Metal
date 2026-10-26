#nullable enable
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using MacDisplay.ViewModels;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for Vec3Control.axaml.cs
/// </summary>
public partial class Vec3Control : UserControl
{
    public static readonly StyledProperty<double> XValueProperty =
        AvaloniaProperty.Register<Vec3Control, double>(nameof(XValue), defaultBindingMode: BindingMode.TwoWay);

    public double XValue
    {
        get => GetValue(XValueProperty);
        set => SetValue(XValueProperty, value);
    }

    public static readonly StyledProperty<double> YValueProperty =
        AvaloniaProperty.Register<Vec3Control, double>(nameof(YValue), defaultBindingMode: BindingMode.TwoWay);

    public double YValue
    {
        get => GetValue(YValueProperty);
        set => SetValue(YValueProperty, value);
    }

    public static readonly StyledProperty<double> ZValueProperty =
        AvaloniaProperty.Register<Vec3Control, double>(nameof(ZValue), defaultBindingMode: BindingMode.TwoWay);

    public double ZValue
    {
        get => GetValue(ZValueProperty);
        set => SetValue(ZValueProperty, value);
    }

    public static readonly StyledProperty<Vec3ParamViewModel?> Vec3SettingsProperty =
        AvaloniaProperty.Register<Vec3Control, Vec3ParamViewModel?>(nameof(Vec3Settings));

    public Vec3ParamViewModel? Vec3Settings
    {
        get => GetValue(Vec3SettingsProperty);
        set => SetValue(Vec3SettingsProperty, value);
    }

    public Vec3Control()
    {
        InitializeComponent();
    }
}