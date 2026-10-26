#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Avalonia.Input.Platform;

using CommunityToolkit.Mvvm.ComponentModel;

using IFSEngine.Generation;
using IFSEngine.Model;
using IFSEngine.Rendering;
using IFSEngine.Serialization;

using MacDisplay.Helper;
using MacDisplay.Properties;
using MacDisplay.Serialization;

namespace MacDisplay.Models;

/// <summary>
/// The main workspace model that contains a <see cref="RendererGL"/> 
/// and an <see cref="IFS"/> that it is rendering.
/// </summary>
public partial class Workspace : ObservableObject
{
    private readonly IFSHistoryTracker _tracker = new();
    private List<string> _includeSources = [];
    private List<TransformPlugin> _loadedTransforms = [];
    private List<EffectPlugin> _loadedEffects = [];

    // Base directories fallback configuration
    public static string TemplatesDirectoryPath => Path.Combine(AppContext.BaseDirectory, "Templates");
    public static string LibraryDirectoryPath => Path.Combine(AppContext.BaseDirectory, "Library");
    public static string IncludesDirectoryPath => Path.Combine(AppContext.BaseDirectory, "Includes");
    public static string TransformsDirectoryPath => Path.Combine(AppContext.BaseDirectory, "Transforms");
    public static string PostEffectsDirectoryPath => Path.Combine(AppContext.BaseDirectory, "PostEffects");

    public event EventHandler<string>? StatusTextChanged;
    public event EventHandler? LoadedParamsChanged;
    public event EventHandler<int>? ParamSourceRemoved;
    public IReadOnlyCollection<TransformPlugin> LoadedTransforms => _loadedTransforms;
    public IReadOnlyCollection<EffectPlugin> LoadedEffects => _loadedEffects;
    public IReadOnlyCollection<string> IncludeSources => _includeSources;
    public IReadOnlyDictionary<string, int[]> ResolutionPresets { get; private set; } = new Dictionary<string, int[]>();
    public IReadOnlyDictionary<string, string> FfmpegPresets { get; private set; } = new Dictionary<string, string>();
    public Author CurrentUser { get; set; } = Author.Unknown;
    public bool TransparentBackground { get; set; } = false;
    public bool IsFinalRenderingMode = false;
    public bool InvertAxisY;
    public double Sensitivity;
    public bool UseWhiteForBlankParams;
    public bool IsRawFrameExportEnabled { get; private set; }
    public bool IsExportVideoFileEnabled { get; private set; }
    public string? FfmpegPath { get; private set; }
    public string? FfmpegArgs { get; private set; }
    public bool SaveMetadata { get; private set; }
    public bool IncludeParamsInMetadata { get; private set; }
    public string? EditedFilePath { get; private set; }
    public static IReadOnlyList<string> RecentFilePaths
    {
        get
        {
            var paths = new string[Settings.Default.RecentFiles.Count];
            Settings.Default.RecentFiles.CopyTo(paths, 0);
            return paths;
        }
    }
    [ObservableProperty] private bool _hasUnsavedChanges;

    private RendererGL _renderer = null!;
    public RendererGL Renderer
    {
        get => _renderer;
        set
        {
            SetProperty(ref _renderer, value);
            if (Ifs != null)
                _renderer.LoadParams(Ifs);
        }
    }

    public IFS Ifs { get; private set; } = null!;
    public FlamePalette? Palette => Ifs?.Palette;

    public bool IsHistoryUndoable => _tracker.IsHistoryUndoable;
    public bool IsHistoryRedoable => _tracker.IsHistoryRedoable;

    public static string[] TemplateFilePaths => Directory.Exists(TemplatesDirectoryPath) ? Directory.GetFiles(TemplatesDirectoryPath, "*.ifsjson") : [];

    /// <summary>
    /// Call <see cref="Initialize"/> before using
    /// </summary>
    /// <param name="r"></param>
    public Workspace(RendererGL r)
    {
        Renderer = r;
    }

    public async Task Initialize()
    {
        await ApplyUserSettings();
        await LoadLibrary();
        if (Renderer != null) 
        {            
            await Renderer.Initialize(_includeSources, _loadedTransforms, _loadedEffects);

            Ifs = new IFS();
            Renderer.LoadParams(Ifs);
        }
    }

    private async Task LoadLibrary()
    {
        await LoadSourceLibrary();
        
        var resPath = Path.Combine(LibraryDirectoryPath, "ResolutionPresets.json");
        if (File.Exists(resPath))
        {
            ResolutionPresets = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int[]>>(
                await File.ReadAllTextAsync(resPath)) ?? [];
        }

        var ffmpegPath = Path.Combine(LibraryDirectoryPath, "FfmpegPresets.json");
        if (File.Exists(ffmpegPath))
        {
            FfmpegPresets = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(
                await File.ReadAllTextAsync(ffmpegPath)) ?? [];
        }
    }

    private async Task LoadSourceLibrary()
    {
        //load includes
        var includeFiles = Directory.Exists(IncludesDirectoryPath) ? Directory.GetFiles(IncludesDirectoryPath) : [];
        var loadIncludeTasks = includeFiles.Select(file => File.ReadAllTextAsync(file));
        _includeSources = (await Task.WhenAll(loadIncludeTasks)).ToList();
        var includeNames = includeFiles.Select(i => Path.GetFileName(i)).ToList();

        //load transform plugins
        var transformFiles = Directory.Exists(TransformsDirectoryPath) 
            ? Directory.GetFiles(TransformsDirectoryPath, "*.ifstf", SearchOption.AllDirectories) 
            : [];
        var loadTransformTasks = transformFiles.Select(file => TransformPlugin.FromFile(file));
        var allTransforms = (await Task.WhenAll(loadTransformTasks)).ToList();
        var skippedTransforms = allTransforms.Where(t => t.IncludeUses.Any(u => !includeNames.Contains(u)));
        _loadedTransforms = allTransforms.Except(skippedTransforms).OrderBy(e => e.Name).ToList();
        OnPropertyChanged(nameof(LoadedTransforms));

        //load postfx plugins
        var postFxFiles = Directory.Exists(PostEffectsDirectoryPath) 
            ? Directory.GetFiles(PostEffectsDirectoryPath, "*.ifsfx", SearchOption.AllDirectories) 
            : [];
        var loadPostFxTasks = postFxFiles.Select(file => EffectPlugin.FromFile(file));
        var allPostFxs = (await Task.WhenAll(loadPostFxTasks)).ToList();
        var skippedPostFxs = allPostFxs.Where(t => t.IncludeUses.Any(u => !includeNames.Contains(u)));
        _loadedEffects = allPostFxs.Except(skippedPostFxs).OrderBy(e => e.Name).ToList();
        OnPropertyChanged(nameof(LoadedEffects));
    }

    public async Task ReloadPlugins()
    {
        await LoadSourceLibrary();
        Ifs.ReloadPlugins(LoadedTransforms, LoadedEffects);
        await Renderer.LoadPlugins(_includeSources, LoadedTransforms, LoadedEffects);
        Renderer.StartRenderLoop();
        OnPropertyChanged(nameof(LoadedTransforms));
        OnPropertyChanged(nameof(LoadedEffects));
    }

    public void LoadParams(IFS ifs, string? loadedFilePath)
    {
        if (Ifs != null)
        {
            TakeSnapshot();
        }

        _renderer?.LoadParams(ifs);

        if (Ifs?.Iterators != null)
        {
            foreach (var i in Ifs.Iterators)
            {
                var pos = i.GetPosition();
                if (pos != null)
                {
                    ifs.Iterators.FirstOrDefault(i2 => i2.Id == i.Id)?.SetPosition(pos);
                }
            }
        }

        Ifs = ifs;
        SetEditedFilePath(loadedFilePath);
        HasUnsavedChanges = false;
        
        if (_renderer != null && !_renderer.IsRendering)
            _renderer.StartRenderLoop();
            
        LoadedParamsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Ifs));
    }
    
    /// <param name="isTemplate">Whether to consider the loaded file as a template so it cannot be saved over. Pass false to consider it being edited.</param>
    public async Task LoadParamsFileAsync(string path, bool isTemplate)
    {
        IFS ifs;
        try
        {
            ifs = await IfsNodesSerializer.LoadJsonFileAsync(path, LoadedTransforms, LoadedEffects, false);
        }
        catch (System.Runtime.Serialization.SerializationException ex)
            when (ex.InnerException is AggregateException exs)
        {
            var unknownPluginNames = exs.InnerExceptions.Select(e => ((UnknownPluginException)e).PluginName);
            if (unknownPluginNames.All(pluginName => LoadedTransforms.Any(t => t.Name == pluginName) || LoadedEffects.Any(t => t.Name == pluginName)))
            {
                ifs = await IfsNodesSerializer.LoadJsonFileAsync(path, LoadedTransforms, LoadedEffects, true);
                await DialogHelper.ShowMessageBoxAsync("Warning", $"The loaded file was created using different versions of the following plugins:\r\n{string.Join(", ", unknownPluginNames)}.");
            }
            else
                throw;
        }
        LoadParams(ifs, isTemplate ? null : path);
    }

    public void RaiseAnimationFrameChanged() => OnPropertyChanged("AnimationFrame");

    public void LoadBlankParams()
    {
        var blankParams = new IFS();
        if (!UseWhiteForBlankParams)
            blankParams.Palette = Generator.GenerateRandomIqPalette(true);
        LoadParams(blankParams, null);
    }

    public void LoadRandomParams()
    {
        Generator g = new Generator(LoadedTransforms);
        IFS r = g.GenerateOne(new GeneratorOptions());
        r.ImageResolution = new System.Drawing.Size(1920, 1080);
        LoadParams(r, null);
    }

    public async Task SaveParamsAsync()
    {
        if (EditedFilePath == null)
            throw new InvalidOperationException();
        await SaveParamsAsAsync(EditedFilePath);
    }

    public async Task SaveParamsAsAsync(string path)
    {
        Ifs.AddAuthor(CurrentUser);
        await IfsNodesSerializer.SaveJsonFileAsync(Ifs, path);
        SetEditedFilePath(path);
        HasUnsavedChanges = false;
    }

    private void SetEditedFilePath(string? path)
    {
        EditedFilePath = path;
        if (path != null)
        {
            var paths = Settings.Default.RecentFiles;
            if (paths.Contains(path))
                paths.Remove(path);
            paths.Add(path);
            if (paths.Count > 5)
                paths.RemoveAt(0);
            Settings.Default.Save();
            OnPropertyChanged(nameof(RecentFilePaths));
        }
        OnPropertyChanged(nameof(EditedFilePath));
    }

    public async Task CopyToClipboard()
    {
        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
        if (clipboard != null)
        {
            string jsonData = IfsNodesSerializer.SerializeJsonString(Ifs);
            await clipboard.SetTextAsync(jsonData);
        }
    }

    public async Task PasteFromClipboard()
    {
        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
        if (clipboard != null)
        {
            string? jsonData = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(jsonData))
            {
                IFS ifs = IfsNodesSerializer.DeserializeJsonString(jsonData, LoadedTransforms, LoadedEffects, true);
                LoadParams(ifs, null);
            }
        }
    }

    public void UndoHistory()
    {
        Ifs = _tracker.Undo(Ifs, LoadedTransforms, LoadedEffects);
        _renderer?.LoadParams(Ifs);
        LoadedParamsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Ifs));
    }

    public void RedoHistory()
    {
        Ifs = _tracker.Redo(Ifs, LoadedTransforms, LoadedEffects);
        _renderer?.LoadParams(Ifs);
        LoadedParamsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Ifs));
    }

    public void ClearHistory() => _tracker.Clear();

    public void TakeSnapshot()
    {
        if (Ifs != null)
        {
            _tracker.TakeSnapshot(Ifs);
            HasUnsavedChanges = true;
        }
    }

    public async Task ApplyUserSettings()
    {
        try
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
            
            if (!config.HasFile)
            {
                // Ensure the target directory structure exists in Application Support
                var fileInfo = new FileInfo(config.FilePath);
                if (fileInfo.Directory != null)
                {
                    if (!fileInfo.Directory.Exists)
                    {
                        fileInfo.Directory.Create();
                    }

                    // Locate the default config template next to the running assembly
                    var appConfigPath = Path.Combine(AppContext.BaseDirectory, "MacDisplay.dll.config");

                    if (File.Exists(appConfigPath))
                    {
                        // Copy the default config directly to the user config path
                        File.Copy(appConfigPath, config.FilePath, true);
                        Console.WriteLine($"[Config] Initialized user config from default template: {config.FilePath}");
                    }
                    else
                    {
                        // Fallback if the source template isn't found
                        try { Settings.Default.Upgrade(); } catch { }
                        Settings.Default.Save();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config Warning] Could not initialize user settings: {ex.Message}");
        }

        if (Renderer != null)
        {
            Renderer.EnablePerceptualUpdates = Settings.Default.PerceptuallyUniformUpdates;
            await Renderer.SetWorkgroupCount(Settings.Default.WorkgroupCount);
            Renderer.TargetFramerate = Settings.Default.TargetFramerate;
        }
        
        InvertAxisY = Settings.Default.InvertAxisY;
        Sensitivity = Settings.Default.Sensitivity;
        UseWhiteForBlankParams = Settings.Default.UseWhiteForBlankParams;
        IsRawFrameExportEnabled = Settings.Default.IsRawFrameExportEnabled;
        IsExportVideoFileEnabled = Settings.Default.IsExportVideoFileEnabled;
        FfmpegPath = Settings.Default.FfmpegPath;
        FfmpegArgs = Settings.Default.FfmpegArgs;
        SaveMetadata = Settings.Default.SaveMetadata;
        IncludeParamsInMetadata = Settings.Default.IncludeParamsInMetadata;
        CurrentUser = new Author
        {
            Name = Settings.Default.AuthorName,
            Link = Settings.Default.AuthorLink
        };
    }

    public void SetFinalResolution(int imageWidth, int imageHeight)
    {
        Ifs.ImageResolution = new System.Drawing.Size(imageWidth, imageHeight);
        if (IsFinalRenderingMode)
            Renderer.SetHistogramScale(1.0);
        else
            Renderer.SetHistogramScaleToDisplay();
        OnPropertyChanged(nameof(Ifs.ImageResolution));
    }

    public async Task<int> ImportAnimationChannels(string csvFilePath)
    {
        var csv = (await File.ReadAllLinesAsync(csvFilePath)).Select(row => row.Split(',').Select(field => field.Trim()).ToArray()).ToArray();
        var channelPaths = csv[0].ToArray();
        foreach (var channelPath in channelPaths)
            Ifs.Dopesheet.Channels.Remove(channelPath);
        for (int row = 1; row < csv.Length; row++)
        {
            if (csv[row].Length != channelPaths.Length)
                continue;
            var frameTime = TimeOnly.FromTimeSpan(TimeSpan.FromSeconds((row - 1.0) / Ifs.Dopesheet.Fps));
            for (int column = 0; column < channelPaths.Length; column++)
            {
                var field = csv[row][column];
                var val = double.Parse(field, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
                Ifs.Dopesheet.AddOrUpdateChannel(channelPaths[column], channelPaths[column], frameTime, val);
            }
        }
        return channelPaths.Length;
    }

    public void UpdateStatusText(string statusText) => StatusTextChanged?.Invoke(this, statusText);

    public void OnParamSourceRemoved(int sourceId) => ParamSourceRemoved?.Invoke(this, sourceId);
}