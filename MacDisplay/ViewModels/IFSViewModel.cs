#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.IO;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using IFSEngine.Model;

using MacDisplay.Helper;
using MacDisplay.Models;
using MacDisplay.Views;

using static MacDisplay.Helper.ForceDirectedGraphLayout;

using TransformPlugin = IFSEngine.Model.TransformPlugin;

namespace MacDisplay.ViewModels;

public partial class IFSViewModel : ObservableObject
{
    private readonly Workspace _workspace;

    public IEnumerable<object> NodeMapElements => _connectionViewModels.Cast<object>().Concat(_iteratorViewModels);
    public IEnumerable<TransformPlugin> FilteredTransforms => FilterTransforms(_workspace.LoadedTransforms);
    public IReadOnlyCollection<ConnectionViewModel> ConnectionViewModels => _connectionViewModels;

    [ObservableProperty] private IteratorViewModel? _connectingIterator;

    [NotifyPropertyChangedFor(nameof(FilteredTransforms))]
    [ObservableProperty] private string _transformSearchFilter = "";

    private readonly ObservableCollection<IteratorViewModel> _iteratorViewModels = [];
    private readonly ObservableCollection<ConnectionViewModel> _connectionViewModels = [];
    private IteratorViewModel? _selectedIterator;
    public IteratorViewModel? SelectedIterator
    {
        get => _selectedIterator;
        set
        {
            if (_selectedIterator != null)
                _selectedIterator.IsSelected = false;

            SetProperty(ref _selectedIterator, value);

            OnPropertyChanged(nameof(IsIteratorEditorVisible));
            DuplicateSelectedCommand.NotifyCanExecuteChanged();
            SplitSelectedCommand.NotifyCanExecuteChanged();

            if (_selectedIterator != null)
            {
                SelectedConnection = null;
                _selectedIterator.IsSelected = true;
            }
        }
    }

    private bool _hasIteratorSelection => SelectedIterator != null;
    public bool IsIteratorEditorVisible => SelectedIterator != null;

    private ConnectionViewModel? _selectedConnection;
    public ConnectionViewModel? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (_selectedConnection != null)
                _selectedConnection.IsSelected = false;

            SetProperty(ref _selectedConnection, value);
            OnPropertyChanged(nameof(IsConnectionEditorVisible));

            if (_selectedConnection != null)
            {
                SelectedIterator = null;
                _selectedConnection.IsSelected = true;
            }
        }
    }

    public bool IsConnectionEditorVisible => SelectedConnection != null;

    public Color BackgroundColor
    {
        get
        {
            if (_workspace.Ifs == null) return Colors.Black;
            var c = _workspace.Ifs.BackgroundColor;
            return Color.FromArgb(255, c.R, c.G, c.B);
        }
        set
        {
            if (_workspace.Ifs != null)
            {
                _workspace.Ifs.BackgroundColor = System.Drawing.Color.FromArgb(255, value.R, value.G, value.B);
                _workspace.Renderer?.InvalidateDisplay();
                OnPropertyChanged(nameof(BackgroundColor));
            }
        }
    }

    public FlamePalette? Palette => _workspace.Ifs?.Palette;

    private ValueSliderSettings? _fogEffect;
    public ValueSliderSettings FogEffectSlider => _fogEffect ??= new()
    {
        Label = "🌫 Fog effect",
        ToolTip = "Fades parts that are out of focus.",
        DefaultValue = IFS.Default.FogEffect,
        ValueWillChange = _workspace.TakeSnapshot,
        ValueChanged = (v) => _workspace.Renderer?.InvalidateHistogramBuffer(),
        AnimationPath = "FogEffect",
        MinValue = 0,
    };

    public IFSViewModel(Workspace workspace)
    {
        _workspace = workspace;
        _iteratorViewModels.CollectionChanged += (s, e) => workspace.Renderer?.InvalidateParamsBuffer();
        workspace.LoadedParamsChanged += (s, e) =>
        {
            int? selectedIteratorId = SelectedIterator?.Iterator.Id;
            int? selectedConnectionFromId = SelectedConnection?.from.Iterator.Id;
            int? selectedConnectionToId = SelectedConnection?.to.Iterator.Id;

            SelectedIterator = null;
            SelectedConnection = null;
            HandleIteratorsChanged();

            SelectedIterator = _iteratorViewModels.FirstOrDefault(i => i.Iterator.Id == selectedIteratorId);
            SelectedConnection = _connectionViewModels.FirstOrDefault(i => i.from.Iterator.Id == selectedConnectionFromId && i.to.Iterator.Id == selectedConnectionToId);

            OnPropertyChanged(string.Empty);
        };

        if (_workspace.Ifs?.Iterators != null)
        {
            _workspace.Ifs.Iterators.ToList().ForEach(i => AddNewIteratorVM(i));
            HandleIteratorsChanged();
        }
    }

    private IteratorViewModel AddNewIteratorVM(Iterator i)
    {
        var ivm = new IteratorViewModel(i, _workspace)
        {
            RemoveCommand = RemoveIteratorCommand,
            DuplicateCommand = DuplicateCommand,
            SplitCommand = SplitCommand,
        };
        ivm.ConnectingStarted += Iterator_ConnectingStarted;
        ivm.ConnectingEnded += Iterator_ConnectingEnded;
        if (SelectedIterator != null)
        {
            ivm.Position = new BindablePoint(
                SelectedIterator.Position.X + SelectedIterator.NodeSize / 1.5 + ivm.NodeSize / 1.5,
                SelectedIterator.Position.Y);
        }
        _iteratorViewModels.Add(ivm);
        return ivm;
    }

    private void Iterator_ConnectingStarted(object? sender, EventArgs e)
    {
        if (ConnectingIterator is null)
        {
            ConnectingIterator = (IteratorViewModel)sender!;
            ConnectingIterator.Redraw();
        }
    }

    private void Iterator_ConnectingEnded(object? sender, EventArgs e)
    {
        if (ConnectingIterator is null)
        {
            return;
        }

        var ivm = (IteratorViewModel)sender!;
        _workspace.TakeSnapshot();
        
        double currentWeight = ConnectingIterator.Iterator.WeightTo[ivm.Iterator];

        if (currentWeight > 0.0)
            ConnectingIterator.Iterator.WeightTo[ivm.Iterator] = 0.0;
        else
            ConnectingIterator.Iterator.WeightTo[ivm.Iterator] = 1.0;

        _workspace.Renderer?.InvalidateParamsBuffer();

        HandleIteratorsChanged();
        SelectedConnection = _connectionViewModels.FirstOrDefault(c => c.from == ConnectingIterator && c.to == ivm);
        ConnectingIterator = null;
    }

    private void HandleIteratorsChanged()
    {
        if (_workspace.Ifs?.Iterators == null) return;

        //remove nodes
        var removedIteratorVMs = _iteratorViewModels.Where(vm => !_workspace.Ifs.Iterators.Any(i => vm.Iterator == i)).ToList();
        removedIteratorVMs.ForEach(vm =>
        {
            _iteratorViewModels.Remove(vm);
        });
        //remove connections
        var removedConnections = _connectionViewModels.Where(c => !c.from.Iterator.WeightTo.TryGetValue(c.to.Iterator, out double ww) || ww == 0.0 || !_iteratorViewModels.Any(i => i == c.from) || !_iteratorViewModels.Any(i => i == c.to)).ToList();
        removedConnections.ForEach(vm2 => _connectionViewModels.Remove(vm2));
        //add nodes
        var newIterators = _workspace.Ifs.Iterators.Where(i => !_iteratorViewModels.Any(vm => vm.Iterator == i)).ToList();
        newIterators.ForEach(i => AddNewIteratorVM(i));
        //add connections:
        _iteratorViewModels.ToList().ForEach(HandleConnectionsChanged);
        Redraw();
        OnPropertyChanged(nameof(NodeMapElements));
    }

    private void HandleConnectionsChanged(IteratorViewModel vm)
    {
        var newConnections = vm.Iterator.WeightTo.Where(w => w.Value > 0.0 && !_connectionViewModels.Any(c => c.from == vm && c.to.Iterator == w.Key)).ToList();
        foreach (var c in newConnections)
        {
            var cvm = new ConnectionViewModel(_connectionViewModels, vm, _iteratorViewModels.First(vm2 => vm2.Iterator == c.Key), _workspace);
            cvm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Weight")
                {
                    _workspace.Renderer?.InvalidateParamsBuffer();
                    HandleIteratorsChanged(); //ugh
                }
            };
            _connectionViewModels.Add(cvm);
        }
        vm.RaiseConnectionPropertyChanged();
    }

    public void Redraw()
    {
        foreach (var i in _iteratorViewModels)
        {
            i.Redraw();
        }
    }

    private IEnumerable<TransformPlugin> FilterTransforms(IReadOnlyCollection<TransformPlugin> transforms)
    {
        if (string.IsNullOrEmpty(TransformSearchFilter))
            return transforms;
        var startsWithResults = transforms.Where(tr => tr.Name.StartsWith(TransformSearchFilter, StringComparison.InvariantCultureIgnoreCase));
        var containsResults = transforms.Where(tr => tr.Name.Contains(TransformSearchFilter, StringComparison.InvariantCultureIgnoreCase));
        var tagResults = transforms.Where(tr => tr.Tags.Any(tag => tag.Contains(TransformSearchFilter, StringComparison.InvariantCultureIgnoreCase)));
        var searchResults = startsWithResults.Concat(containsResults.Concat(tagResults)).Distinct();
        return searchResults;
    }

    [RelayCommand]
    private void AddIterator(TransformPlugin tf)
    {
        // If no fractal state is loaded yet, initialize a blank workspace first
        if (_workspace.Ifs == null)
        {
            _workspace.LoadBlankParams();
        }

        _workspace.TakeSnapshot();
        Iterator newIterator = new(tf);
        _workspace.Ifs!.AddIterator(newIterator, false);
        if (SelectedIterator != null)
        {
            SelectedIterator.Iterator.WeightTo[newIterator] = 1.0;
            newIterator.WeightTo[SelectedIterator.Iterator] = 1.0;
        }
        _workspace.Renderer?.InvalidateParamsBuffer();
        HandleIteratorsChanged();
        SelectedIterator = _iteratorViewModels.First(vm => vm.Iterator == newIterator);
        
        if (_workspace.Renderer?.IsRendering == false) 
            _workspace.Renderer.StartRenderLoop();
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedConnection is not null)
        {
            _workspace.TakeSnapshot();
            SelectedConnection.from.Iterator.WeightTo[SelectedConnection.to.Iterator] = 0.0;
            _workspace.Renderer?.InvalidateParamsBuffer();
            SelectedConnection = null;
            HandleIteratorsChanged();
        }
        else if (SelectedIterator is not null)
            RemoveIteratorCommand.Execute(SelectedIterator);
    }

    [RelayCommand]
    private void RemoveIterator(IteratorViewModel vm)
    {
        _workspace.TakeSnapshot();
        _workspace.Ifs.RemoveIterator(vm.Iterator);
        _workspace.Renderer?.InvalidateParamsBuffer();
        if (SelectedIterator == vm)
            SelectedIterator = null;
        HandleIteratorsChanged();
        _workspace.OnParamSourceRemoved(vm.Iterator.Id);
    }

    [RelayCommand(CanExecute = nameof(_hasIteratorSelection))]
    private void DuplicateSelected() => Duplicate(SelectedIterator!);

    [RelayCommand]
    private void Duplicate(IteratorViewModel vm)
    {
        _workspace.TakeSnapshot();
        Iterator dupe = _workspace.Ifs.DuplicateIterator(vm.Iterator, splitWeights: false);
        _workspace.Renderer?.InvalidateParamsBuffer();
        HandleIteratorsChanged();
        SelectedIterator = _iteratorViewModels.First(vm => vm.Iterator == dupe);
    }

    [RelayCommand(CanExecute = nameof(_hasIteratorSelection))]
    private void SplitSelected() => Split(SelectedIterator!);

    [RelayCommand]
    private void Split(IteratorViewModel vm)
    {
        _workspace.TakeSnapshot();
        Iterator dupe = _workspace.Ifs.DuplicateIterator(vm.Iterator, splitWeights: true);
        _workspace.Renderer?.InvalidateParamsBuffer();
        HandleIteratorsChanged();
        SelectedIterator = _iteratorViewModels.First(vm => vm.Iterator == dupe);
    }

    [RelayCommand]
    private async Task LoadPalette()
    {
        var (success, path) = await DialogHelper.ShowOpenPaletteDialogAsync();
        if (success)
        {
            await LoadPaletteFromFile(path);
        }
    }

    [RelayCommand]
    private async Task DropPalette(string path) => await LoadPaletteFromFile(path);

    private async Task LoadPaletteFromFile(string path)
    {
        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var picker = new PaletteDialogWindow
        {
            Palettes = await FlamePalette.FromFileAsync(path)
        };
        
        var result = mainWindow is not null ? await picker.ShowDialog<bool>(mainWindow) : false;
        if (result)
        {
            _workspace.TakeSnapshot();
            _workspace.Ifs.Palette = picker.SelectedPalette;
            _workspace.Renderer?.InvalidateParamsBuffer();
            OnPropertyChanged(nameof(Palette));
            Redraw();
            _workspace.UpdateStatusText($"Palette file loaded - {path}");
        }
    }

    [RelayCommand]
    private async Task ReloadTransforms()
    {
        try
        {
            await _workspace.ReloadPlugins();
            _workspace.UpdateStatusText("Transforms reloaded.");
        }
        catch (Exception)
        {
            _workspace.UpdateStatusText("Failed to reload transforms.");
            return;
        }
        foreach (IteratorViewModel ivm in _iteratorViewModels)
            ivm.ReloadParameters();
        OnPropertyChanged(nameof(FilteredTransforms));
    }

    [RelayCommand]
    private static void OpenTransformsDirectory()
    {
        string dirPath = Path.Combine(AppContext.BaseDirectory, "Transforms");
        Directory.CreateDirectory(dirPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = dirPath,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private static void EditTransformSource(string filePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void SelectConnection(ConnectionViewModel con) => SelectedConnection = con;

    [RelayCommand]
    private void SelectIterator(IteratorViewModel it) => SelectedIterator = it;

    private bool _canExecuteUndo => _workspace.IsHistoryUndoable;
    [RelayCommand(CanExecute = nameof(_canExecuteUndo))]
    private void Undo() => _workspace.UndoHistory();

    private bool _canExecuteRedo => _workspace.IsHistoryRedoable;
    [RelayCommand(CanExecute = nameof(_canExecuteRedo))]
    private void Redo() => _workspace.RedoHistory();

    [RelayCommand]
    private void TakeSnapshot() => _workspace.TakeSnapshot();

    [RelayCommand]
    private void AutoLayoutNodes()
    {
        var vertices = _iteratorViewModels.Select(v => new Vector2((float)v.Position.X, (float)v.Position.Y)).ToList();
        var edges = _connectionViewModels.Where(e => !e.IsLoopback).Select(e => (_iteratorViewModels.IndexOf(e.from), _iteratorViewModels.IndexOf(e.to))).ToList();
        Graph graph = new(vertices, edges);
        var nodePositions = GenerateLayout(graph, 1.0, 1.0, 0.000001);
        var avg = new Vector2(
            nodePositions.Average(x => x.X),
            nodePositions.Average(x => x.Y));
        nodePositions = nodePositions.ConvertAll(p => p - avg + new Vector2(500, 500));
        for (int i = 0; i < nodePositions.Count; i++)
        {
            _iteratorViewModels[i].Position = new BindablePoint(nodePositions[i].X, nodePositions[i].Y);
        }
    }
}