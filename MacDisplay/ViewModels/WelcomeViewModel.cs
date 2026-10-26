#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using IFSEngine.Generation;
using IFSEngine.Model;
using IFSEngine.Rendering;
using IFSEngine.Utility;

using OpenTK.Windowing.Desktop;

using MacDisplay.Properties;
using MacDisplay.Serialization;
using MacDisplay.Services;

namespace MacDisplay.ViewModels;

public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly IReadOnlyCollection<string> _includeSources;
    private readonly IReadOnlyCollection<TransformPlugin> _loadedTransforms;
    private readonly IReadOnlyCollection<EffectPlugin> _loadedEffects;

    public RelayCommand? ContinueCommand { get; set; }
    public WelcomeWorkflow SelectedWorkflow { get; private set; } = WelcomeWorkflow.FromScratch;
    public IFS ExploreParams { get; private set; } = new IFS();
    public string? SelectedFilePath { get; private set; }

    public string SelectedExpander
    {
        get => Settings.Default.WelcomeExpanderState;
        set
        {
            Settings.Default.WelcomeExpanderState = value;
            Settings.Default.Save();
            OnPropertyChanged(nameof(SelectedExpander));
        }
    }

    [ObservableProperty] public partial IImage? ExploreThumbnail { get; set; }
    [ObservableProperty] public partial List<KeyValuePair<IFS, IImage>> Templates { get; set; } = [];
    [ObservableProperty] public partial List<KeyValuePair<IFS, IImage>> RecentFiles { get; set; } = [];

    public WelcomeViewModel(IReadOnlyCollection<string> includeSources, IReadOnlyCollection<TransformPlugin> loadedTransforms, IReadOnlyCollection<EffectPlugin> loadedEffects)
    {
        _includeSources = includeSources;
        _loadedTransforms = loadedTransforms;
        _loadedEffects = loadedEffects;
    }

    public async Task Initialize()
    {
        RendererGL? renderer = null;
        try
        {
            // Try initializing offscreen OpenGL context for thumbnails
            GameWindow hw = new(new GameWindowSettings(), new NativeWindowSettings
            {
                Flags = OpenTK.Windowing.Common.ContextFlags.Offscreen,
                IsEventDriven = true,
                StartFocused = false,
                StartVisible = false,
                WindowState = OpenTK.Windowing.Common.WindowState.Minimized
            });
            renderer = new RendererGL(hw.Context);
            renderer.SetDisplayResolution(100, 100);
            await renderer.Initialize(_includeSources, _loadedTransforms, _loadedEffects);
            await renderer.SetWorkgroupCount(100);
        }
        catch (Exception ex)
        {
            // Safely fallback if GPU/OpenGL context creation fails on this platform
            System.Diagnostics.Debug.WriteLine($"[Warning] OpenGL thumbnail renderer failed to initialize: {ex.Message}");
            renderer = null;
        }

        ExploreParams = new Generator(_loadedTransforms).GenerateOne(new());
        ExploreThumbnail = renderer != null ? RenderThumbnail(renderer, ExploreParams) : CreateFallbackThumbnail();

        // Load template files and recent files.
        List<Task<IFS>> templateFilesTasks = [];
        List<Task<IFS>> recentFilesTasks = [];
        try
        {
            var templatesDir = Path.Combine(AppContext.BaseDirectory, "Templates");
            if (Directory.Exists(templatesDir))
            {
                templateFilesTasks = Directory
                    .GetFiles(templatesDir, "*.ifsjson")
                    .Select(templatePath => IfsNodesSerializer.LoadJsonFileAsync(templatePath, _loadedTransforms, _loadedEffects, true))
                    .ToList();
            }

            var recentFilePaths = Settings.Default.RecentFiles.Cast<string>();
            recentFilesTasks = recentFilePaths
                .Select(recentPath => IfsNodesSerializer.LoadJsonFileAsync(recentPath, _loadedTransforms, _loadedEffects, true))
                .Reverse()
                .ToList();
            
            await Task.WhenAll(templateFilesTasks.Concat(recentFilesTasks));
        }
        catch (System.Runtime.Serialization.SerializationException) { /*Ignore broken files*/ }
        catch (FileNotFoundException) { /*Ignore deleted recent files*/ }

        // Render thumbnails or use fallbacks if renderer is unavailable
        Templates = templateFilesTasks
            .Where(t => t.IsCompletedSuccessfully).Select(t => t.Result)
            .ToDictionary(p => p, p => renderer != null ? RenderThumbnail(renderer, p) : CreateFallbackThumbnail())
            .ToList();

        RecentFiles = recentFilesTasks
            .Where(t => t.IsCompletedSuccessfully).Select(t => t.Result)
            .ToDictionary(p => p, p => renderer != null ? RenderThumbnail(renderer, p) : CreateFallbackThumbnail())
            .ToList();
    }

    private static IImage RenderThumbnail(RendererGL renderer, IFS ifs)
    {
        var previewIfs = ifs.DeepClone();
        previewIfs.ImageResolution = new System.Drawing.Size(100, 100);
        previewIfs.Entropy = 1.0 / 100;
        previewIfs.Warmup = 0;
        
        renderer.LoadParams(previewIfs);
        renderer.SetHistogramScaleToDisplay();
        renderer.DispatchCompute();
        renderer.RenderImage();

        var wbm = new WriteableBitmap(new PixelSize(renderer.HistogramWidth, renderer.HistogramHeight), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var locked = wbm.Lock())
        {
            renderer.CopyPixelDataToBitmap(locked.Address).Wait();
        }
        return wbm;
    }

    private static IImage CreateFallbackThumbnail()
    {
        return new WriteableBitmap(new PixelSize(100, 100), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
    }

    [RelayCommand]
    private void SelectWorkflow(WelcomeWorkflow selection)
    {
        SelectedWorkflow = selection;
        ContinueCommand?.Execute(null);
    }

    [RelayCommand]
    private void SelectExploreWorkflow(IFS? ifs)
    {
        if (RecentFiles.Any(f => f.Key == ifs))
        {
            SelectedWorkflow = WelcomeWorkflow.LoadRecent;
            SelectedFilePath = Settings.Default.RecentFiles[RecentFiles.Count - 1 - RecentFiles.IndexOf(RecentFiles.First(f => f.Key == ifs))];
        }
        else
            SelectedWorkflow = WelcomeWorkflow.Explore;
        if (ifs is not null)
            ExploreParams = ifs;
        ContinueCommand?.Execute(null);
    }

    [RelayCommand]
    private async Task PasteFromClipboard()
    {
        try
        {
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow?.Clipboard is { } clipboard)
            {
                string jsonData = await clipboard.TryGetTextAsync() ?? string.Empty;
                if (!string.IsNullOrEmpty(jsonData))
                {
                    IFS ifs = IfsNodesSerializer.DeserializeJsonString(jsonData, _loadedTransforms, _loadedEffects, true);
                    SelectExploreWorkflow(ifs);
                }
            }
        }
        catch (SerializationException) { /* Ignore when Clipboard contains no params */ }
        catch (Exception) { /* Ignore general clipboard/parse exceptions */ }
    }


    [RelayCommand]
    private void DropFile(DragEventArgs e)
    {
        if (FileDropHandler.IsDropSupported(e, out var fileType, out var filePath) && 
            (fileType.ToString().Contains("Image", StringComparison.OrdinalIgnoreCase) || 
             fileType.ToString().Contains("Params", StringComparison.OrdinalIgnoreCase)))
        {
            SelectedWorkflow = WelcomeWorkflow.LoadFromFile;
            SelectedFilePath = filePath;
            ContinueCommand?.Execute(null);
        }
    }

    [RelayCommand]
    private void DisableStartup()
    {
        Settings.Default.IsWelcomeShownOnStartup = false;
        Settings.Default.Save();
        ContinueCommand?.Execute(null);
    }
}