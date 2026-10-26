#nullable enable
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MacDisplay.ViewModels;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for TitleBox.axaml
/// </summary>
public partial class TitleBox : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public TitleBox()
    {
        InitializeComponent();
    }

    private void titleTextBox_GotKeyboardFocus(object? sender, FocusChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() => titleTextBox.SelectAll());
    }

    private void titleTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Return or Key.Enter)
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
        }
        else if (e.Key == Key.Escape)
        {
            if (Vm is not null)
            {
                Vm.IFSTitle = Vm.workspace.Ifs.Title;
            }
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
        }
    }

    private void ClosePopup_Click(object? sender, RoutedEventArgs e)
    {
        authorsToggle.IsChecked = false;
    }
}