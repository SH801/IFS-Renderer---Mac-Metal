#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Cavern;
using MacDisplay.ViewModels;

namespace MacDisplay.Views.Animation;

/// <summary>
/// Interaction logic for AnimationPanel.axaml
/// </summary>
public partial class AnimationPanel : UserControl
{
    private AnimationViewModel _vm => (AnimationViewModel)DataContext!;
    private DrawingBrush scrubberTicksBrush => (DrawingBrush)scrubberTicksGrid.Background!;
    private CancellationTokenSource _drawingCts = new();

    public AnimationPanel()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (_vm != null)
            {
                _vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName is nameof(_vm.ViewScale) or nameof(_vm.Audio))
                        Dispatcher.UIThread.InvokeAsync(RedrawScrubberVisuals);
                };
                Dispatcher.UIThread.InvokeAsync(RedrawScrubberVisuals);
            }
        };
    }

    private async Task RedrawScrubberVisuals()
    {
        scrubberTicksBrush.SourceRect = new RelativeRect(new Point(0, 0), new Size(_vm.ViewScale, 1), RelativeUnit.Absolute);
        scrubberTicksBrush.DestinationRect = new RelativeRect(new Point(0, 0), new Size(_vm.ViewScale, 1), RelativeUnit.Absolute);

        if (_vm.Audio is not null)
        {
            var clip = _vm.Audio.Clip;
            var viewScale = _vm.ViewScale;
            await _drawingCts.CancelAsync();
            _drawingCts = new CancellationTokenSource();
            
            var audioBitmap = await Task.Run(() => CreateAudioBarsDrawing(clip, viewScale, _drawingCts.Token));
            if (audioBitmap != null && scrubberAudioBarsContainer.Background is ImageBrush brush)
            {
                brush.Source = audioBitmap;
            }
        }
    }

    private static Bitmap? CreateAudioBarsDrawing(Clip audioClip, double viewScale, CancellationToken cancellationToken = default)
    {
        int totalWidth = (int)(audioClip.Length * viewScale);
        if (totalWidth <= 0 || totalWidth > 16384) return null;

        int height = 30;
        var bitmap = new WriteableBitmap(new PixelSize(totalWidth, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var locked = bitmap.Lock())
        {
            // Direct buffer pixel drawing logic can go here
        }

        return bitmap;
    }

    private void ChannelHeaderScroller_ScrollChanged(object? sender, ScrollChangedEventArgs e) 
        => sheetScroller.Offset = new Vector(sheetScroller.Offset.X, channelHeaderScroller.Offset.Y);

    private void ChannelGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {

        var point = e.GetCurrentPoint(sender as Visual);
        if (point.Properties.IsRightButtonPressed)
        {
            var pos = point.Position;
            // Convert pixel X position to time using your ViewScale
            double time = pos.X / _vm.ViewScale;
            
            // Store it so that InsertKeyframe() has a valid value to read
            _vm.KeyframeInsertPosition = time;
        }
    }

    private void TimeScrubber_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint((Visual?)sender);
        if (point.Properties.IsLeftButtonPressed && e.Source == sender)
        {
            var rawT = e.GetPosition((Visual?)sender).X / _vm.ViewScale;
            var t = Math.Clamp(rawT, 0.0, _vm.ClipLength);
            
            _vm.JumpToTime(t);
            e.Handled = true;
        }
    }

    private void TimeScrubber_PointerMoved(object? sender, PointerEventArgs e)
    {

        var point = e.GetCurrentPoint((Visual?)sender);
        if (point.Properties.IsLeftButtonPressed && e.Source == sender)
        {
            var rawT = e.GetPosition((Visual?)sender).X / _vm.ViewScale;
            
            // Clamp between 0 and the clip length to prevent negative or excessive tick errors
            var t = Math.Clamp(rawT, 0.0, _vm.ClipLength);
            
            _vm.JumpToTime(t);
        }
    }

    private void TimeScrubber_PointerWheelChanged(object? sender, PointerWheelEventArgs e) 
    {
        // Only zoom if the Shift key is pressed
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _vm.ViewScale = Math.Clamp(_vm.ViewScale * (e.Delta.Y > 0 ? 1.2f : 1 / 1.2f), 1.0f, 300.0f);
            e.Handled = true; // Optional: marks the event as handled so container scrolling doesn't conflict
        }
    }
}