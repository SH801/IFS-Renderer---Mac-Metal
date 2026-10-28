using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using IFSEngine.Model;
using IFSEngine.Rendering;

using MacDisplay.Helper;
using MacDisplay.Models;
using MacDisplay.Properties;
using MacDisplay.Serialization;
using MacDisplay.Services;
using MacDisplay.ViewModels;
using MacDisplay.Views;

namespace MacDisplay;

/// <summary>
/// Interaction logic for MainWindow.axaml
/// </summary>
public partial class MainWindow : Window
{
    private EditorWindow? _editorWindow;
    private GeneratorWindow? _generatorWindow;
    private MainViewModel vm => (MainViewModel)DataContext!;
    private bool _rendererInitialized = false;
    private RendererGL? _renderer;

    public MainWindow()
    {
        InitializeComponent();

        mainDisplay.DisplayResolutionChanged += mainDisplay_DisplayResolutionChanged;

        TestingComponent.TestRequested += () =>
        {
            _renderer?.Test();
        };        
    }

    private async void MainWindow_ContentRendered(object? sender, RoutedEventArgs e)
    {
        FixAutoDockHeight();
        
        if (mainDisplay is null)
            throw new InvalidOperationException("Failed to initialize graphics context.");

        // 1. Initialize your renderer (passing whatever context/view mainDisplay requires)
        _renderer = new RendererGL(mainDisplay); 
        mainDisplay.AttachRenderer(_renderer);

        // 2. Pass the real renderer into the workspace instead of null!
        var workspace = new Workspace(_renderer); 
        await workspace.Initialize();

        var workflow = WelcomeWorkflow.FromScratch;
        
        if (App.OpenVerbPath is not null)
        {
            IFS ifs;
            try
            {
                ifs = IfsNodesSerializer.LoadJsonFile(App.OpenVerbPath, workspace.LoadedTransforms, workspace.LoadedEffects, true);
            }
            catch (SerializationException)
            {
                ifs = new IFS();
            }
            workspace.LoadParams(ifs, App.OpenVerbPath);
        }
        else if (Settings.Default.IsWelcomeShownOnStartup)
        {
            var welcomeViewModel = new WelcomeViewModel(workspace.IncludeSources, workspace.LoadedTransforms, workspace.LoadedEffects);
            var welcomeWindow = new WelcomeWindow
            {
                DataContext = welcomeViewModel
            };
            await welcomeWindow.ShowDialog(this);
            Focus();

            workflow = welcomeViewModel.SelectedWorkflow;
            if (workflow == WelcomeWorkflow.Explore)
                workspace.LoadParams(welcomeViewModel.ExploreParams, null);
            else if (workflow == WelcomeWorkflow.LoadRecent)
                workspace.LoadParams(welcomeViewModel.ExploreParams, welcomeViewModel.SelectedFilePath);
            else if (workflow == WelcomeWorkflow.LoadFromFile && welcomeViewModel.SelectedFilePath != null)
            {
                try
                {
                    var ext = Path.GetExtension(welcomeViewModel.SelectedFilePath);
                    if (ext == ".ifsjson")
                    {
                        await workspace.LoadParamsFileAsync(welcomeViewModel.SelectedFilePath, false);
                    }
                    else if (ext == ".png")
                    {
                        if (PngMetadataHelper.TryExtractParamsFromImage(welcomeViewModel.SelectedFilePath, workspace.LoadedTransforms, workspace.LoadedEffects, out var ifs))
                            workspace.LoadParams(ifs, null);
                    }
                }
                catch
                {
                    workspace.UpdateStatusText($"Failed to load params - {welcomeViewModel.SelectedFilePath}");
                }
            }
        }

        DataContext = new MainViewModel(workspace, workflow);

        if (workflow == WelcomeWorkflow.BrowseRandoms)
            OpenGeneratorWindow();
        if (workflow == WelcomeWorkflow.VisitSettings)
            OpenSettingsWindow();

        vm.AnimationViewModel.Channels.CollectionChanged += (s, e) =>
        {
            if (vm.workspace.Ifs.Dopesheet.Channels.Count > 0)
                ShowAnimationsPanel();
        };
    }

    private void FixAutoDockHeight()
    {
        if (tonemappingPane != null) tonemappingPane.Height = double.NaN;
        if (environmentPane != null) environmentPane.Height = double.NaN;
        if (performancePane != null) performancePane.Height = double.NaN;
    }

    public void GeneratorMenuItem_Click(object? sender, RoutedEventArgs e) => OpenGeneratorWindow();
    public void EditorMenuItem_Click(object? sender, RoutedEventArgs e) => OpenEditorWindow();
    public void SettingsMenuItem_Click(object? sender, RoutedEventArgs e) => OpenSettingsWindow();
    public void AboutMenuItem_Click(object? sender, RoutedEventArgs e) => OpenAboutWindow();

    private void OpenGeneratorWindow()
    {
        if (_generatorWindow == null || !(_generatorWindow.IsLoaded))
        {
            _generatorWindow = new GeneratorWindow
            {
                DataContext = new GeneratorViewModel(vm)
            };
        }

        _generatorWindow.Show();
        if (!_generatorWindow.IsActive)
            _generatorWindow.Activate();
    }

    private void OpenEditorWindow()
    {
        if (_editorWindow == null || !(_editorWindow.IsLoaded))
        {
            _editorWindow = new EditorWindow
            {
                DataContext = vm.IFSViewModel
            };
        }

        _editorWindow.Show();
        if (!_editorWindow.IsActive)
            _editorWindow.Activate();
    }

    private void OpenSettingsWindow()
    {
        var settingsWindow = new SettingsWindow
        {
            DataContext = new SettingsViewModel(vm)
        };
        settingsWindow.ShowDialog(this);
    }

    private void OpenAboutWindow()
    {
        var dialog = new AboutDialogWindow();
        dialog.ShowDialog(this);
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (vm?.workspace?.HasUnsavedChanges == true)
        {
            var dialog = new ExitDialogWindow();
            await dialog.ShowDialog(this);
        }

        _generatorWindow?.Close();
        _editorWindow?.Close();

        if (vm != null)
            await vm.DisposeAsync();

        base.OnClosing(e);
    }

    private void MainWindow_DragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = FileDropHandler.GetDragDropEffect(e);
    }

    private void MainWindow_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        vm?.DropFileCommand?.Execute(e.DataTransfer);
    }

    private async void mainDisplay_GamepadConnectionStateChanged(object sender, bool e)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => vm.IsGamepadConnected = e);
    }

    private void mainDisplay_DisplayResolutionChanged(object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        Console.WriteLine($"[MainWindow] mainDisplay_DisplayResolutionChanged -> Previous: {e.PreviousSize.Width}x{e.PreviousSize.Height} | New: {e.NewSize.Width}x{e.NewSize.Height}");

        // Ensure we have valid, non-zero dimensions
        if (e.NewSize.Width > 1 && e.NewSize.Height > 1)
        {
            if (!_rendererInitialized)
            {
                // Trigger your initialization logic from ContentRendered once bounds are valid
                MainWindow_ContentRendered(sender, e);
                _rendererInitialized = true;
            }
            else
            {
                // Subsequent size changes dynamically update the renderer resolution
                _renderer?.SetDisplayResolution((int)e.NewSize.Width, (int)e.NewSize.Height);
            }
        }

        // Your existing view model call
        vm?.QualitySettingsViewModel?.UpdatePreviewRenderSettings();
    }

    private void ShowAnimationsPanel()
    {
        if (animationsPanel != null)
        {
            animationsPanel.IsVisible = true;
        }
    }
}