#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using IFSEngine.Model;
using IFSEngine.Serialization;
using IFSEngine.Utility;

using MacDisplay.Helper;
using MacDisplay.Models;
using MacDisplay.Services;

namespace MacDisplay.ViewModels;

public record TemplateItem(string Name, string Path);

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    internal readonly Workspace workspace;
    public IFS? Ifs => workspace.Ifs;

    public ToneMappingViewModel ToneMappingViewModel { get; }
    public CameraSettingsViewModel CameraSettingsViewModel { get; }
    public PerformanceViewModel PerformanceViewModel { get; }
    public QualitySettingsViewModel QualitySettingsViewModel { get; }
    public IFSViewModel IFSViewModel { get; }
    public AnimationViewModel AnimationViewModel { get; }
    public PostFxPanelViewModel PostFxPanelViewModel { get; }

    public bool TransparentBackground
    {
        get => workspace.TransparentBackground;
        set
        {
            workspace.TakeSnapshot();
            workspace.TransparentBackground = value;
            if (value)
                IFSViewModel.BackgroundColor = Colors.Black;
            OnPropertyChanged(nameof(TransparentBackground));
            OnPropertyChanged(nameof(IsColorPickerEnabled));
        }
    }

    [ObservableProperty] private string _statusBarText = string.Empty;
    [ObservableProperty] private bool _isHintsPanelVisible = true;
    [ObservableProperty] private bool _isGamepadConnected = false;

    public string IsRenderingIcon => workspace.Renderer?.IsRendering == true ? "||" : "▶️";
    
    public string IterationLevel => workspace.Renderer != null 
        ? BitOperations.Log2(1 + workspace.Renderer.TotalIterations / (ulong)(Math.Max(1, workspace.Renderer.HistogramWidth) * Math.Max(1, workspace.Renderer.HistogramHeight))).ToString("00.") 
        : "00.";

    public double IterationProgressPercent
    {
        get
        {
            Task.Delay(50).ContinueWith(t => OnPropertyChanged(nameof(IterationProgressPercent)));
            if (workspace.Renderer == null || workspace.Ifs == null) return 0.0;
            double denom = Math.Pow(2, workspace.Ifs.TargetIterationLevel) - 1;
            if (denom <= 0) return 0.0;
            return 100.0 * (workspace.Renderer.TotalIterations / (double)(Math.Max(1, workspace.Renderer.HistogramWidth) * Math.Max(1, workspace.Renderer.HistogramHeight))) / denom;
        }
    }

    public bool IsColorPickerEnabled => !TransparentBackground;
    public bool InvertAxisY => workspace.InvertAxisY;
    public float Sensitivity => (float)workspace.Sensitivity;

    public string WindowTitle => workspace.Ifs is null ? "IFSRenderer" : $"{workspace.Ifs.Title}{(workspace.HasUnsavedChanges ? '*' : string.Empty)} - IFSRenderer";

    public string IFSTitle
    {
        get => workspace.Ifs?.Title ?? "Untitled";
        set
        {
            if (workspace.Ifs != null && workspace.Ifs.Title != value)
            {
                workspace.TakeSnapshot();
                workspace.Ifs.Title = value;
                OnPropertyChanged(nameof(IFSTitle));
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public IEnumerable<Author> AuthorList => workspace.Ifs?.Authors ?? [];

    public static IEnumerable<string> RecentFilePaths => Workspace.RecentFilePaths.Reverse();
    public static bool HasRecentFiles => RecentFilePaths.Any();
    public static IEnumerable<TemplateItem> TemplateItems => 
        Workspace.TemplateFilePaths.Select(path => new TemplateItem(Path.GetFileNameWithoutExtension(path), path));
    public static IReadOnlyDictionary<string, string> Templates => Workspace.TemplateFilePaths.ToDictionary(path => path, path => Path.GetFileNameWithoutExtension(path));

    public MainViewModel(Workspace workspace, WelcomeWorkflow workflow)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        workspace.PropertyChanged += (s, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(this.workspace.HasUnsavedChanges): OnPropertyChanged(nameof(WindowTitle)); break;
            }
        };
        workspace.StatusTextChanged += (s, e) => StatusBarText = e;
        
        PerformanceViewModel = new PerformanceViewModel(workspace);
        QualitySettingsViewModel = new QualitySettingsViewModel(workspace);
        IFSViewModel = new IFSViewModel(workspace);
        AnimationViewModel = new AnimationViewModel(workspace);
        CameraSettingsViewModel = new CameraSettingsViewModel(workspace);
        CameraSettingsViewModel.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);
        ToneMappingViewModel = new ToneMappingViewModel(workspace);
        ToneMappingViewModel.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);
        PostFxPanelViewModel = new PostFxPanelViewModel(workspace);

        if (workflow == WelcomeWorkflow.ShowFileDialog)
            ShowLoadParamsDialogCommand?.Execute(this);

        if (workspace.Renderer != null)
        {
            workspace.Renderer.DisplayFramebufferUpdated += (s, e) => OnPropertyChanged(nameof(IterationLevel));
        }

        workspace.UpdateStatusText("Initialized");
    }

    [RelayCommand]
    private async Task StartStopRendering()
    {
        if (workspace.Renderer == null) return;

        if (workspace.Renderer.IsRendering)
        {
            await workspace.Renderer.StopRenderLoop();
            workspace.UpdateStatusText("Stopped rendering");
        }
        else
        {
            workspace.Renderer.StartRenderLoop();
            workspace.UpdateStatusText("Started rendering");
        }
        OnPropertyChanged(nameof(IsRenderingIcon));
    }

    [RelayCommand]
    private void New()
    {
        workspace.LoadBlankParams();
        workspace.UpdateStatusText("Blank parameters loaded");
        OnPropertyChanged(nameof(IsRenderingIcon));
    }

    [RelayCommand]
    private void LoadRandom()
    {
        workspace.LoadRandomParams();
        workspace.UpdateStatusText("Randomly generated parameters loaded");
        OnPropertyChanged(nameof(IsRenderingIcon));
    }

    [RelayCommand]
    private async Task SaveParams()
    {
        if (workspace.EditedFilePath is not null)
        {
            await workspace.SaveParamsAsync();
            workspace.UpdateStatusText($"Parameters saved to {workspace.EditedFilePath}");
        }
        else
            await SaveParamsAs();
    }

    [RelayCommand]
    private async Task SaveParamsAs()
    {
        if (workspace.Ifs == null) return;

        var (success, path) = await DialogHelper.ShowSaveParamsDialogAsync(workspace.Ifs.Title);
        if (success)
        {
            if (IFSTitle == "Untitled")
                IFSTitle = Path.GetFileNameWithoutExtension(path);

            try
            {
                await workspace.SaveParamsAsAsync(path);
                workspace.UpdateStatusText($"Parameters saved to {path}");
            }
            catch (Exception)
            {
                workspace.UpdateStatusText("ERROR - Failed to save params.");
            }
        }
    }

    [RelayCommand]
    private async Task ShowLoadParamsDialog()
    {
        var (success, path) = await DialogHelper.ShowOpenParamsDialogAsync();
        if (success)
        {
            await LoadParamsFromFile(path, false);
        }
    }

    [RelayCommand]
    private void CopyClipboardParams()
    {
        _ = workspace.CopyToClipboard();
        workspace.UpdateStatusText("Parameters copied to Clipboard");
    }

    [RelayCommand]
    private void PasteClipboardParams()
    {
        try
        {
            _ = workspace.PasteFromClipboard();
            workspace.UpdateStatusText("Parameters pasted from Clipboard");
        }
        catch (SerializationException)
        {
            workspace.UpdateStatusText("ERROR - Failed to paste params from Clipboard");
        }
        OnPropertyChanged(nameof(IsRenderingIcon));
    }

    [RelayCommand]
    private async Task DropFile(DragEventArgs e)
    {
        if (FileDropHandler.IsDropSupported(e, out var fileType, out var filePath))
        {
            string fileTypeString = fileType.ToString();
            if (fileTypeString.Contains("Params", StringComparison.OrdinalIgnoreCase))
            {
                await LoadParamsFromFile(filePath, false);
            }
            else if (fileTypeString.Contains("Palette", StringComparison.OrdinalIgnoreCase))
            {
                IFSViewModel.DropPaletteCommand.Execute(filePath);
            }
            else if (fileTypeString.Contains("Transform", StringComparison.OrdinalIgnoreCase))
            {
                var transformsDir = Path.Combine(AppContext.BaseDirectory, "Transforms");
                Directory.CreateDirectory(transformsDir);
                File.Copy(filePath, Path.Combine(transformsDir, Path.GetFileName(filePath)), true);
                IFSViewModel.ReloadTransformsCommand.Execute(null);
            }
        }
    }

    [RelayCommand]
    private async Task LoadTemplate(string path) => await LoadParamsFromFile(path, true);

    [RelayCommand]
    private async Task LoadParamsFromFile(string path) => await LoadParamsFromFile(path, false);

    private async Task LoadParamsFromFile(string path, bool isTemplate)
    {
        try
        {
            await workspace.LoadParamsFileAsync(path, isTemplate);
            workspace.UpdateStatusText($"Parameters loaded from {path}");
        }
        catch (SerializationException ex)
            when (ex.InnerException is AggregateException exs)
        {
            var unknownTransforms = exs.InnerExceptions.Select(e => (UnknownPluginException)e);
            workspace.UpdateStatusText($"ERROR - Missing transforms: {string.Join(", ", unknownTransforms.Select(t => t.PluginName))}");
        }
        catch (SerializationException ex)
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "error.log");
            File.WriteAllText(logPath, ex.ToString());
            workspace.UpdateStatusText($"ERROR - Failed to load params: {path}. See log: {logPath}");
        }
        OnPropertyChanged(nameof(IsRenderingIcon));
    }

    [RelayCommand]
    private async Task ExportToClipboard()
    {
        if (workspace.Renderer == null) return;
        var bitmap = await workspace.Renderer.GetExportBitmapSource(TransparentBackground);
        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow?.Clipboard is { } clipboard)
        {
            workspace.UpdateStatusText("Image exported to clipboard");
        }

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
    }

    [RelayCommand]
    private async Task SaveImage()
    {
        if (workspace.Renderer == null || workspace.Ifs == null) return;

        workspace.UpdateStatusText("Exporting...");
        var makeBitmapTask = workspace.Renderer.GetExportBitmapSource(TransparentBackground);

        var (success, path) = await DialogHelper.ShowExportImageDialogAsync(workspace.Ifs.Title);
        if (success)
        {
            workspace.Ifs.AddAuthor(workspace.CurrentUser);
            var bitmap = await makeBitmapTask;
            
            bitmap.Save(path, PngBitmapEncoderOptions.Default);

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            workspace.UpdateStatusText($"Image exported to {path}");
        }
        else
            workspace.UpdateStatusText(string.Empty);

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
    }

    [RelayCommand]
    private async Task SaveExr()
    {
        if (workspace.Renderer == null || workspace.Ifs == null) return;

        workspace.UpdateStatusText("Exporting...");
        Task<float[][][]> getDataTask = Task.Run(workspace.Renderer.ReadHistogramData);

        var (success, path) = await DialogHelper.ShowExportExrDialogAsync(workspace.Ifs.Title);
        if (success)
        {
            var histogramData = await getDataTask;
            using var fstream = File.Create(path);
            OpenEXR.WriteStream(fstream, histogramData);
            workspace.UpdateStatusText($"Image exported to {path}");
        }
        else
            workspace.UpdateStatusText(string.Empty);

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
    }

    [RelayCommand]
    private void TakeSnapshot()
    {
        IsHintsPanelVisible = false;
        workspace.TakeSnapshot();
    }

    [RelayCommand]
    private void InteractionFinished() => CameraSettingsViewModel.RaiseCameraParamsChanged();

    [RelayCommand]
    private static void ExitApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (workspace.Renderer != null)
        {
            await workspace.Renderer.DisposeAsync();
        }
    }

    [RelayCommand]
    private static void VisitIssues() => Process.Start(new ProcessStartInfo("https://github.com/bezo97/IFSRenderer/issues/") { UseShellExecute = true });

    [RelayCommand]
    private static void VisitForum() => Process.Start(new ProcessStartInfo("https://github.com/bezo97/IFSRenderer/discussions") { UseShellExecute = true });

    [RelayCommand]
    private static void ReportBug() => Process.Start(new ProcessStartInfo("https://github.com/bezo97/IFSRenderer/issues/new?assignees=&labels=&template=bug_report.md") { UseShellExecute = true });

    [RelayCommand]
    private static void CheckUpdates() => Process.Start(new ProcessStartInfo("https://github.com/bezo97/IFSRenderer/releases") { UseShellExecute = true });

    [RelayCommand]
    private static void VisitWiki() => Process.Start(new ProcessStartInfo("https://github.com/bezo97/IFSRenderer/wiki") { UseShellExecute = true });
}