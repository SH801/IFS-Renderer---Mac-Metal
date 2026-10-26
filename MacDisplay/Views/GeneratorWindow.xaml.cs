using Avalonia.Controls;
using MacDisplay.ViewModels;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for GeneratorWindow.axaml
/// </summary>
public partial class GeneratorWindow : Window
{
    public GeneratorWindow()
    {
        InitializeComponent();

        Opened += async (s, e) =>
        {
            if (DataContext is GeneratorViewModel viewModel)
            {
                await viewModel.Initialize();
            }
        };
    }
}