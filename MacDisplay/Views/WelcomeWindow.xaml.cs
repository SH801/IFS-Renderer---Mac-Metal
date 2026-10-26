#nullable enable
using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using MacDisplay.Services;
using MacDisplay.ViewModels;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for WelcomeWindow.axaml.cs
/// </summary>
public partial class WelcomeWindow : Window
{
    private WelcomeViewModel? Vm => DataContext as WelcomeViewModel;

    public WelcomeWindow()
    {
        Console.WriteLine("[WelcomeWindow] Initializing component...");

        InitializeComponent();
        
        DataContextChanged += (s, e) =>
        {
            if (DataContext is WelcomeViewModel viewModel)
            {
                viewModel.ContinueCommand = new RelayCommand(() =>
                {
                    Close(true);
                });
            }
        };

        Opened += async (s, e) =>
        {
            try
            {
                if (Vm is not null)
                {
                    await Vm.Initialize();
                }
            }
            catch (Exception ex)
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "error.log");
                File.WriteAllText(logPath, ex.ToString());
                System.Diagnostics.Debug.WriteLine($"WelcomeWindow Initialization Error: {ex}");
            }
        };

        // Register drag and drop handlers using Avalonia's helper methods
        DragDrop.AddDragOverHandler(this, WelcomeWindow_DragOver);
        DragDrop.AddDropHandler(this, WelcomeWindow_Drop);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Handle Paste shortcut (Ctrl+V / Cmd+V)
        var keyModifiers = e.KeyModifiers;
        bool isCtrlOrCmd = keyModifiers.HasFlag(KeyModifiers.Control) || keyModifiers.HasFlag(KeyModifiers.Meta);
        
        if (isCtrlOrCmd && e.Key == Key.V)
        {
            if (Vm?.PasteFromClipboardCommand.CanExecute(null) == true)
            {
                Vm.PasteFromClipboardCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void WelcomeWindow_DragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = FileDropHandler.GetDragDropEffect(e);
    }

    private void WelcomeWindow_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Vm is not null)
        {
            Vm.DropFileCommand.Execute(e);
        }
    }
}