using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace MacDisplay.Views;

public partial class AboutDialogWindow : Window
{
    public AboutDialogWindow()
    {
        InitializeComponent();
    }

    private ICommand? _okCommand;
    public ICommand OkCommand =>
        _okCommand ??= new RelayCommand(OnOkCommand);
        
    private void OnOkCommand() => Close(true);
}