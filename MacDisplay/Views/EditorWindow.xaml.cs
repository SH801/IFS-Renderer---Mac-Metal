#nullable enable
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MacDisplay.Services;
using MacDisplay.ViewModels;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for EditorWindow.axaml
/// </summary>
public partial class EditorWindow : Window
{
    private IFSViewModel? Vm => DataContext as IFSViewModel;

    public EditorWindow()
    {
        InitializeComponent();
    }

    private void Undo_Executed() => Vm?.UndoCommand?.Execute(null);

    private bool Undo_CanExecute() => Vm?.UndoCommand?.CanExecute(null) ?? false;

    private void Redo_Executed() => Vm?.RedoCommand?.Execute(null);

    private bool Redo_CanExecute() => Vm?.RedoCommand?.CanExecute(null) ?? false;

    private void editorWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = FileDropHandler.GetDragDropEffect(e); 
    }

    private void editorWindow_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.DataContext is MainViewModel mainVm)
        {
            mainVm.DropFileCommand.Execute(e.DataTransfer);
        }
    }
}