#nullable enable
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using IFSEngine.Model;

namespace MacDisplay.Views;

public partial class PaletteDialogWindow : Window
{
    public PaletteDialogWindow()
    {
        InitializeComponent();
    }

    public FlamePalette? SelectedPalette { get; set; }

    public static readonly StyledProperty<List<FlamePalette>?> PalettesProperty =
        AvaloniaProperty.Register<PaletteDialogWindow, List<FlamePalette>?>(nameof(Palettes));

    public List<FlamePalette>? Palettes
    {
        get => GetValue(PalettesProperty);
        set => SetValue(PalettesProperty, value);
    }

    private RelayCommand<FlamePalette?>? _paletteSelectedCommand;
    public RelayCommand<FlamePalette?> PaletteSelectedCommand =>
        _paletteSelectedCommand ??= new RelayCommand<FlamePalette?>((FlamePalette? selected) =>
        {
            SelectedPalette = selected;
            Close(true);
        });

    private RelayCommand? _cancelDialogCommand;
    public RelayCommand CancelDialogCommand =>
        _cancelDialogCommand ??= new RelayCommand(() =>
        {
            SelectedPalette = null;
            Close(false);
        });
}