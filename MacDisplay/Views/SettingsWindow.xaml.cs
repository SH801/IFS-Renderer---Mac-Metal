#nullable enable
using Avalonia.Controls;
using MacDisplay.ViewModels;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for SettingsWindow.axaml
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Opened += (s, e) =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.SettingsSaved += (s2, e2) => Close(true);
                vm.SettingsCanceled += (s2, e2) => Close(false);
            }
        };
    }
}