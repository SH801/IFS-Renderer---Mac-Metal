using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MacDisplay.Views;

public partial class ExitDialogWindow : Window
{
    public ExitChoice ExitDialogResult { get; set; } = ExitChoice.Cancel;

    public ExitDialogWindow()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ExitDialogResult = ExitChoice.Save;
        Close(true);
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        ExitDialogResult = ExitChoice.Quit;
        Close(true);
    }

    public enum ExitChoice
    {
        Quit,
        Save,
        Cancel
    }
}