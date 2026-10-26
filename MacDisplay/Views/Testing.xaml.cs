using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace MacDisplay.Views;

public partial class Testing : UserControl
{
    public event Action? TestRequested;

    public Testing()
    {
        InitializeComponent();
    }

    private void TestButton_Click(object? sender, RoutedEventArgs e)
    {
        TestRequested?.Invoke();
    }
}