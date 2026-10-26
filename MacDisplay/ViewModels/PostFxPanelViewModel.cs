#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using IFSEngine.Model;

using MacDisplay.Models;

namespace MacDisplay.ViewModels;

public partial class PostFxPanelViewModel : ObservableObject
{
    private readonly Workspace _workspace;

    [ObservableProperty] public partial ObservableCollection<PostFxLayerViewModel> PostEffectLayers { get; set; } = [];

    /// <summary>
    /// All loaded posteffect plugins available to add as layer.
    /// </summary>
    public IEnumerable<EffectPlugin> LoadedEffects => _workspace?.LoadedEffects ?? [];

    public PostFxPanelViewModel(Workspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _workspace.LoadedParamsChanged += (s, e) => RefreshInstances();
        
        if (_workspace.Ifs != null)
        {
            RefreshInstances();
        }
    }

    private void RefreshInstances()
    {
        PostEffectLayers.Clear();
        if (_workspace?.Ifs?.PostEffects != null)
        {
            foreach (var instance in _workspace.Ifs.PostEffects)
            {
                PostEffectLayers.Add(new PostFxLayerViewModel(instance, _workspace));
            }
        }
        OnPropertyChanged(nameof(PostEffectLayers));
    }

    [RelayCommand]
    private void AddLayer(EffectPlugin? effectPlugin)
    {
        if (effectPlugin == null || _workspace?.Ifs == null) return;
        _workspace.TakeSnapshot();
        _workspace.Ifs.AddPostEffectLayer(new EffectLayer(effectPlugin));
        _workspace.Renderer?.InvalidateDisplay();
        RefreshInstances();
    }

    [RelayCommand]
    private void RemoveLayer(PostFxLayerViewModel vm)
    {
        if (_workspace?.Ifs == null) return;
        _workspace.TakeSnapshot();
        _workspace.Ifs.RemovePostEffectLayer(vm.Instance);
        _workspace.Renderer?.InvalidateDisplay();
        RefreshInstances();
        _workspace.OnParamSourceRemoved(vm.Instance.Id);
    }

    [RelayCommand]
    private void MoveUp(PostFxLayerViewModel vm)
    {
        if (_workspace?.Ifs?.PostEffects == null) return;
        var list = _workspace.Ifs.PostEffects.ToList();
        int index = list.IndexOf(vm.Instance);
        if (index > 0)
        {
            _workspace.TakeSnapshot();
            _workspace.Ifs.MovePostEffectLayer(vm.Instance, index - 1);
            _workspace.Renderer?.InvalidateDisplay();
            RefreshInstances();
        }
    }

    [RelayCommand]
    private void MoveDown(PostFxLayerViewModel vm)
    {
        if (_workspace?.Ifs?.PostEffects == null) return;
        var list = _workspace.Ifs.PostEffects.ToList();
        int index = list.IndexOf(vm.Instance);
        if (index < _workspace.Ifs.PostEffects.Count - 1)
        {
            _workspace.TakeSnapshot();
            _workspace.Ifs.MovePostEffectLayer(vm.Instance, index + 1);
            _workspace.Renderer?.InvalidateDisplay();
            RefreshInstances();
        }
    }

    [RelayCommand]
    private async Task ReloadEffects()
    {
        try
        {
            await _workspace.ReloadPlugins();
            RefreshInstances();
            OnPropertyChanged(nameof(LoadedEffects));
        }
        catch (System.Exception ex)
        {
            _workspace?.UpdateStatusText("Failed to reload effect plugins.");
            string logFilePath = Path.Combine(AppContext.BaseDirectory, "error.log");
            File.WriteAllText(logFilePath, ex.ToString());
        }
    }

    [RelayCommand]
    private static void OpenPostFxDirectory()
    {
        string dirPath = Path.Combine(AppContext.BaseDirectory, "PostEffects");
        Directory.CreateDirectory(dirPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = dirPath,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private static void EditPostFxSource(string filePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        });
    }
}