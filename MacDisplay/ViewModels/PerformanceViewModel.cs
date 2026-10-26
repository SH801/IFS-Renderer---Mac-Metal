#nullable enable
using System;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using MacDisplay.Helper;
using MacDisplay.Models;

namespace MacDisplay.ViewModels;

public partial class PerformanceViewModel : ObservableObject
{
    private readonly Workspace _workspace;
    private readonly DispatcherTimer _dt;
    private ulong _lastTotalIters = 0;

    // Safe getters with fallbacks if Renderer is null
    public int Fps { get; private set; } = 0;
    public int InvocationIters => _workspace.Renderer?.InvocationIters ?? 0;
    public string IterationSpeed => _workspace.Renderer != null ? (_workspace.Renderer.TotalIterations - _lastTotalIters).ToKMB() + " /s" : "0 /s";
    public string TotalIterations => _workspace.Renderer?.TotalIterations.ToKMB() ?? "0";

    public PerformanceViewModel(Workspace workspace)
    {
        _workspace = workspace;
        workspace.LoadedParamsChanged += (s, e) => OnPropertyChanged(string.Empty);
        
        // Guard against a null renderer during startup
        if (workspace.Renderer != null)
        {
            workspace.Renderer.DisplayFramebufferUpdated += (s, e) => Fps++;
        }
        
        _dt = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _dt.Tick += (s, e) => UpdateValues();
        _dt.Start();
    }

    public void UpdateValues()
    {
        if (_workspace.Renderer == null) return;

        OnPropertyChanged(nameof(Fps));
        Fps = 0;

        _lastTotalIters = Math.Min(_workspace.Renderer.TotalIterations, _lastTotalIters);
        OnPropertyChanged(nameof(IterationSpeed));
        _lastTotalIters = _workspace.Renderer.TotalIterations;

        OnPropertyChanged(nameof(TotalIterations));
        OnPropertyChanged(nameof(InvocationIters));
    }
}