#nullable enable
using Avalonia.Controls;
using MacDisplay.ViewModels;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for PostFxPanel.axaml
/// </summary>
public partial class PostFxPanel : UserControl
{
    private PostFxPanelViewModel? Vm => DataContext as PostFxPanelViewModel;

    public PostFxPanel()
    {
        InitializeComponent();
    }

    private void AddLayerCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && Vm is not null)
        {
            if (comboBox.SelectedItem is not null)
            {
                Vm.AddLayerCommand.Execute(comboBox.SelectedItem);
                comboBox.SelectedIndex = -1;
            }
        }
    }
}