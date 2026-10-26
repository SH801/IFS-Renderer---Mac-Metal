#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

using Avalonia.Media.Imaging;
using Avalonia.Threading;

using Cavern.Channels;
using Cavern.QuickEQ.Utilities;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using IFSEngine.Model;
using IFSEngine.Utility;

using MacDisplay.Helper;
using MacDisplay.Models;

namespace MacDisplay.ViewModels;

public partial class AnimationViewModel : ObservableObject
{
    public readonly Workspace Workspace;
    private readonly Timer _realtimePlayer;
    public HashSet<KeyframeViewModel> SelectedKeyframes { get; } = [];

    private string? _audioPath;
    public CavernAudio? Audio { get; private set; } = null;
    [ObservableProperty] private ReferenceChannel[] _loadedAudioChannels = [];
    [ObservableProperty] private string? _audioClipTitle = null;
    [ObservableProperty] private double? _keyframeInsertPosition = null;
    [ObservableProperty] private bool _isExportingFrames = false;
    private string? _saveFramesPath = null;

    public TimeOnly CurrentTime { get; private set; } = TimeOnly.MinValue;

    public ObservableCollection<ChannelViewModel> Channels { get; } = [];
    [ObservableProperty] private ChannelViewModel? _editedChannel = null;

    /// <summary>
    /// pixels per second
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SheetWidth))]
    [NotifyPropertyChangedFor(nameof(CurrentTimeScrollPosition))]
    private float _viewScale = 120.0f;

    public float SheetWidth => Workspace.Ifs?.Dopesheet != null 
        ? (float)Workspace.Ifs.Dopesheet.Length.TotalSeconds * ViewScale 
        : 0f;

    public double KeyframeRepositionOffset { get; internal set; }

    [ObservableProperty] private double _currentTimeIncrement = 1.0 / (IFS.Default.Dopesheet?.Fps ?? 30);

    public AnimationViewModel(Workspace workspace)
    {
        Workspace = workspace;
        workspace.LoadedParamsChanged += (s, e) => ReloadDopesheet();
        
        if (workspace.Renderer != null)
        {
            workspace.Renderer.TargetIterationReached += OnFrameFinishedRendering;
        }
        
        workspace.ParamSourceRemoved += OnParamSourceRemoved;
        
        double fps = workspace.Ifs?.Dopesheet?.Fps ?? 30;
        _realtimePlayer = new Timer(TimeSpan.FromSeconds(1.0 / fps).TotalMilliseconds);
        _realtimePlayer.Elapsed += OnPlayerTick;
        _realtimePlayer.AutoReset = true;
    }

    private void OnParamSourceRemoved(object? sender, int sourceId)
    {
        if (Workspace.Ifs?.Dopesheet == null) return;

        var channelsToRemove = Channels.Where(c => c.Path.Contains(sourceId.ToString())).ToList();
        foreach (var ch in channelsToRemove)
        {
            Workspace.Ifs.Dopesheet.Channels.Remove(ch.Path);
            Channels.Remove(ch);
        }
        Workspace.RaiseAnimationFrameChanged();
    }

    public float CurrentTimeScrollPosition => (float)CurrentTime.ToTimeSpan().TotalSeconds * ViewScale;

    public double ClipFps
    {
        get => Workspace.Ifs?.Dopesheet?.Fps ?? 30;
        set
        {
            if (Workspace.Ifs?.Dopesheet != null)
            {
                Workspace.Ifs.Dopesheet.Fps = (int)value;
                _realtimePlayer.Interval = TimeSpan.FromSeconds(1.0 / Workspace.Ifs.Dopesheet.Fps).TotalMilliseconds;
                CurrentTimeIncrement = 1 / value;
            }
        }
    }

    private ValueSliderSettings? _fpsSlider;
    public ValueSliderSettings FpsSlider => _fpsSlider ??= new()
    {
        Label = "🎞 Framerate (fps)",
        ToolTip = "Frames per second",
        DefaultValue = 30,
        Increment = 1,
        MinValue = 1,
        ValueWillChange = Workspace.TakeSnapshot
    };

    public double CurrentTimeSeconds
    {
        get => CurrentTime.ToTimeSpan().TotalSeconds;
        set => JumpToTime(value);
    }

    private ValueSliderSettings? _currentTimeSlider;
    public ValueSliderSettings CurrentTimeSlider => _currentTimeSlider ??= new()
    {
        Label = "⏱️ Time (s)",
        ToolTip = "Current time in seconds",
        DefaultValue = 0.0,
        Increment = 1.0 / (Workspace.Ifs?.Dopesheet?.Fps ?? 30),
        MinValue = 0,
        MaxValue = Workspace.Ifs?.Dopesheet?.Length.TotalSeconds ?? 10
    };

    public void ReloadDopesheet()
    {
        if (Workspace.Ifs?.Dopesheet == null) return;

        EditedChannel = null;
        SelectedKeyframes.Clear();
        Channels.Clear();
        Workspace.Ifs.Dopesheet.Channels.ToList()
            .Select(a => new ChannelViewModel(this, a.Key, a.Value)).ToList().ForEach(Channels.Add);
        OnPropertyChanged(string.Empty);
    }

    public bool? GetChannelCurrentState(string path)
    {
        if (Workspace.Ifs?.Dopesheet?.Channels == null) return null;
        if (!Workspace.Ifs.Dopesheet.Channels.TryGetValue(path, out var channel))
            return null;
        var currentTimeSeconds = CurrentTime.ToTimeSpan().TotalSeconds;
        return channel.Keyframes.Any(kf => kf.t == currentTimeSeconds);
    }

    public bool IsChannelBeingModified(string path, double value)
    {
        if (Workspace.Ifs?.Dopesheet?.Channels == null) return false;
        if (!Workspace.Ifs.Dopesheet.Channels.TryGetValue(path, out var channel))
            return false;
        return Math.Abs(channel.EvaluateAt(CurrentTimeSeconds) - value) > 0.001;
    }

    public double ClipLength
    {
        get => Workspace.Ifs?.Dopesheet?.Length.TotalSeconds ?? 10;
        set
        {
            if (Workspace.Ifs?.Dopesheet != null)
            {
                Workspace.Ifs.Dopesheet.Length = TimeSpan.FromSeconds(value);
                OnPropertyChanged(nameof(ClipLength));
                OnPropertyChanged(nameof(SheetWidth));
            }
        }
    }

    private ValueSliderSettings? _lengthSlider;
    public ValueSliderSettings LengthSlider => _lengthSlider ??= new()
    {
        Label = "↔️ Length (s)",
        ToolTip = "Animation length in seconds",
        DefaultValue = 10,
        Increment = 1,
        MinValue = 1,
        ValueWillChange = Workspace.TakeSnapshot
    };

    public class AnimateValueCommandParameters(string label, string path, double val)
    {
        public string Label { get; } = label;
        public string Path { get; } = path;
        public double Val { get; } = val;
    }

    [RelayCommand]
    public void AnimateValue(AnimateValueCommandParameters param)
    {
        Workspace.TakeSnapshot();
        AddOrUpdateChannel(param.Label, param.Path, param.Val);
    }

    [RelayCommand]
    public void PlayPause()
    {
        _realtimePlayer.Enabled = !_realtimePlayer.Enabled;
    }

    [RelayCommand]
    public void JumpToStart()
    {
        _realtimePlayer.Stop();
        JumpToTime(0);
    }

    [RelayCommand]
    public void JumpToKeyframe(KeyframeViewModel kfv) => JumpToTime(kfv._k.t);

    public void JumpToTime(double t)
    {
        CurrentTime = TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(t));
        Workspace.Ifs?.EvaluateAt(CurrentTime);
        Workspace.Renderer?.InvalidateParamsBuffer();
        Dispatcher.UIThread.Invoke(() =>
        {
            OnPropertyChanged(nameof(CurrentTimeSeconds));
            OnPropertyChanged(nameof(CurrentTimeScrollPosition));
            Workspace.RaiseAnimationFrameChanged();
        });
    }

    private bool JumpToNextFrame()
    {
        if (Workspace.Ifs?.Dopesheet == null) return true;

        var fps = Workspace.Ifs.Dopesheet.Fps > 0 ? Workspace.Ifs.Dopesheet.Fps : 30;
        var nextTimestep = CurrentTime.Add(TimeSpan.FromSeconds(1.0 / fps));
        var lastFrame = nextTimestep.ToTimeSpan() > Workspace.Ifs.Dopesheet.Length;
        if (lastFrame)
        {
            nextTimestep = TimeOnly.MinValue;
        }
        JumpToTime(nextTimestep.ToTimeSpan().TotalSeconds);
        return lastFrame;
    }

    public void AddOrUpdateChannel(string name, string path, double value) => AddOrUpdateChannel(name, path, value, CurrentTime);

    public void AddOrUpdateChannel(string name, string path, double value, TimeOnly position)
    {
        if (Workspace.Ifs?.Dopesheet == null) return;

        EditedChannel = null;
        var vm = Channels.FirstOrDefault(c => c.Path == path);
        if (vm is null)
        {
            Workspace.Ifs.Dopesheet.AddOrUpdateChannel(name, path, position, value);
            Channels.Add(new ChannelViewModel(this, path, Workspace.Ifs.Dopesheet.Channels[path]));
        }
        else
        {
            var positionSeconds = position.ToTimeSpan().TotalSeconds;
            var keyOnFrame = vm.Keyframes.FirstOrDefault(kf => kf._k.Value == value && kf._k.t == positionSeconds);
            if (keyOnFrame is not null)
            {
                if (vm.Keyframes.Count == 1)
                {
                    Workspace.Ifs.Dopesheet.Channels.Remove(vm.Path);
                    Channels.Remove(vm);
                }
                else
                    vm.RemoveKeyframe(keyOnFrame);
            }
            else
                Workspace.Ifs.Dopesheet.AddOrUpdateChannel(name, path, position, value);

            vm.UpdateKeyframes();
        }
        Workspace.RaiseAnimationFrameChanged();
    }

    [RelayCommand]
    public void RemoveKeyframe(KeyframeViewModel kfv)
    {
        if (Workspace.Ifs?.Dopesheet == null) return;

        Workspace.TakeSnapshot();

        if (kfv._cvm.Keyframes.Count == 1)
        {
            Workspace.Ifs.Dopesheet.Channels.Remove(kfv._cvm.Path);
            Channels.Remove(kfv._cvm);
        }
        else
        {
            kfv._cvm.RemoveKeyframe(kfv);
        }
        Workspace.Ifs.EvaluateAt(CurrentTime);
        Workspace.Renderer?.InvalidateParamsBuffer();
        Workspace.RaiseAnimationFrameChanged();
    }

    [RelayCommand]
    public void EditChannel(ChannelViewModel cvm) => EditedChannel = cvm;

    public void CloseChannelEditor() => EditedChannel = null;

    [RelayCommand]
    public void RemoveChannel(ChannelViewModel cvm)
    {
        if (Workspace.Ifs?.Dopesheet == null) return;

        Workspace.TakeSnapshot();
        Workspace.Ifs.Dopesheet.Channels.Remove(cvm.Path);
        Channels.Remove(cvm);
        Workspace.Renderer?.InvalidateParamsBuffer();
        Workspace.RaiseAnimationFrameChanged();
    }

    public void AddToSelection(KeyframeViewModel kvm)
    {
        SelectedKeyframes.Add(kvm);
        kvm.IsSelected = true;
    }

    public void FlipSelection(KeyframeViewModel kvm)
    {
        if (SelectedKeyframes.Contains(kvm))
        {
            SelectedKeyframes.Remove(kvm);
            kvm.IsSelected = false;
        }
        else
        {
            AddToSelection(kvm);
        }
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var kvm in SelectedKeyframes)
            kvm.IsSelected = false;
        SelectedKeyframes.Clear();
    }

    public void PreviewRepositionSelectedKeyframes(double offset)
    {
        if (Workspace.Ifs?.Dopesheet == null || SelectedKeyframes.Count == 0) return;

        var timeOffset = offset / ViewScale;
        timeOffset = Math.Max(timeOffset, -SelectedKeyframes.Min(k => k.KeyframeTime));
        timeOffset = Math.Min(timeOffset, (float)Workspace.Ifs.Dopesheet.Length.TotalSeconds - SelectedKeyframes.Max(k => k.KeyframeTime));
        KeyframeRepositionOffset = timeOffset * ViewScale;
        foreach (var kf in SelectedKeyframes)
            kf.NotifyPositionChanged();
    }

    public void ApplyRepositionOfSelectedKeyframes()
    {
        Workspace.TakeSnapshot();
        foreach (var kf in SelectedKeyframes)
            kf._k.t += KeyframeRepositionOffset / ViewScale;
        KeyframeRepositionOffset = 0.0;
        Workspace.Renderer?.InvalidateParamsBuffer();
    }

    private void OnPlayerTick(object? sender, ElapsedEventArgs e) => JumpToNextFrame();

    [RelayCommand]
    public async Task LoadAudio()
    {
        var (success, path) = await DialogHelper.ShowOpenAudioDialogAsync();
        if (!success)
            return;

        _audioPath = path;
        await Task.Run(() =>
        {
            Audio = new(path);
            LoadedAudioChannels = ChannelPrototype.GetStandardMatrix(Audio.Clip.Channels);
            AudioClipTitle = Audio.Clip.Name ?? Path.GetFileNameWithoutExtension(path);

            OnPropertyChanged(nameof(Audio));
            Workspace.UpdateStatusText($"Audio track loaded successfully - {path}");
        });
    }

    private bool _canExecuteStart => !IsExportingFrames;

    [RelayCommand]
    public async Task StartExportingFrames()
    {
        if (IsExportingFrames) throw new InvalidOperationException();

        _realtimePlayer.Stop();

        var (success, selectedFolderPath) = await DialogHelper.ShowAnimationFolderBrowserDialogAsync();
        if (!success)
            return;
        _saveFramesPath = selectedFolderPath;

        if (Workspace.Renderer?.IsRendering == false)
            Workspace.Renderer.StartRenderLoop();

        IsExportingFrames = true;
        Workspace.Renderer?.InvalidateParamsBuffer();
        Workspace.UpdateStatusText($"Exporting animation frames...");
    }

    private void OnFrameFinishedRendering(object? sender, EventArgs e)
    {
        if (Workspace.Ifs?.Dopesheet == null || Workspace.Renderer == null) return;

        if (IsExportingFrames && _saveFramesPath is not null)
        {
            var frameFileExtension = Workspace.IsRawFrameExportEnabled ? "exr" : "png";
            var frameNr = (int)(CurrentTime.ToTimeSpan().TotalSeconds * Workspace.Ifs.Dopesheet.Fps);
            var framePath = Path.Combine(_saveFramesPath, $"{Workspace.Ifs.Title}-{frameNr:D6}.{frameFileExtension}");

            if (!Workspace.IsRawFrameExportEnabled)
            {
                var bitmap = Workspace.Renderer.GetExportBitmapSource(Workspace.TransparentBackground).Result;
                bitmap.Save(framePath, PngBitmapEncoderOptions.Default);
            }
            else
            {
                var histogramData = Workspace.Renderer.ReadHistogramData().Result;
                using var fstream = File.Create(framePath);
                OpenEXR.WriteStream(fstream, histogramData);
            }

            Workspace.Renderer.Seed = (uint)(Workspace.Ifs.Dopesheet.Fps * (frameNr + 1));

            if (JumpToNextFrame())
            {
                if (Workspace.IsExportVideoFileEnabled)
                    RunFfmpegProcess().Wait();
                IsExportingFrames = false;
                _saveFramesPath = null;
            }
            else
            {
                var totalFrames = Workspace.Ifs.Dopesheet.Length.TotalSeconds * Workspace.Ifs.Dopesheet.Fps;
                Workspace.UpdateStatusText($"Exported frame '{framePath}' ({frameNr + 1}/{totalFrames})");
            }
        }
    }

    [RelayCommand]
    public void StopExportingFrames()
    {
        IsExportingFrames = false;
        _saveFramesPath = null;
        Workspace.UpdateStatusText($"Exporting animation frames stopped.");
    }

    private async Task RunFfmpegProcess()
    {
        if (Workspace.Ifs?.Dopesheet == null) return;
        if (!File.Exists(Workspace.FfmpegPath))
            throw new InvalidOperationException();

        var userArgs = Workspace.FfmpegArgs ?? "";
        var frameFileExtension = Workspace.IsRawFrameExportEnabled ? "exr" : "png";
        var videoFileExtension =
            userArgs.Contains("prores") ? "mov" :
            userArgs.Contains("libvpx-vp9") ? "webm" :
            "mp4";
        var audioArgs = _audioPath is not null ? $"-i \"{_audioPath}\"" : "";

        StringBuilder argsBuilder = new();
        argsBuilder.AppendJoin(' ',
            $"-y",
            $"-nostdin",
            $"-r {Workspace.Ifs.Dopesheet.Fps}",
            $"-i \"{_saveFramesPath}\\{Workspace.Ifs.Title}-%06d.{frameFileExtension}\"",
            audioArgs,
            $"-t {Workspace.Ifs.Dopesheet.Length.TotalSeconds}",
            $"-vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\"",
            userArgs,
            $"\"{Workspace.Ifs.Title}.{videoFileExtension}\"");
        var args = argsBuilder.ToString();

        var ffmpegProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Workspace.FfmpegPath, args)
        {
            WorkingDirectory = _saveFramesPath
        });

        if (ffmpegProc is null)
        {
            Workspace.UpdateStatusText("Video file creation failed - unable to run ffmpeg.");
            return;
        }
        Workspace.UpdateStatusText("Generating video...");
        await ffmpegProc.WaitForExitAsync();
        if (ffmpegProc.ExitCode != 0)
        {
            Workspace.UpdateStatusText($"Video file creation failed - ffmpeg exited with code: {ffmpegProc.ExitCode}.");
            return;
        }
        Workspace.UpdateStatusText($"Video file created successfully at '{_saveFramesPath}'.");
    }

    [RelayCommand]
    public async Task ImportChannels()
    {
        var (success, csvFilePath) = await DialogHelper.ShowImportChannelsDialogAsync();
        if (!success)
            return;

        Workspace.TakeSnapshot();

        var successCount = await Workspace.ImportAnimationChannels(csvFilePath);

        Workspace.Ifs?.EvaluateAt(CurrentTime);
        Workspace.Renderer?.InvalidateParamsBuffer();
        ReloadDopesheet();
        Workspace.RaiseAnimationFrameChanged();

        Workspace.UpdateStatusText($"{successCount} channels successfully imported from file '{csvFilePath}'.");
    }
}