#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using MacDisplay.Helper;
using MacDisplay.Models;

namespace MacDisplay.Views.Animation;

/// <summary>
/// Interaction logic for Spectrogram.axaml
/// </summary>
public partial class Spectrogram : UserControl
{
    private CancellationTokenSource _drawingCts = new();

    public IAudio Audio
    {
        get => GetValue(AudioProperty);
        set => SetValue(AudioProperty, value);
    }
    public static readonly StyledProperty<IAudio> AudioProperty =
        AvaloniaProperty.Register<Spectrogram, IAudio>(nameof(Audio));

    public int SelectedAudioChannelId
    {
        get => GetValue(SelectedAudioChannelIdProperty);
        set => SetValue(SelectedAudioChannelIdProperty, value);
    }
    public static readonly StyledProperty<int> SelectedAudioChannelIdProperty =
        AvaloniaProperty.Register<Spectrogram, int>(nameof(SelectedAudioChannelId), 0);

    public float ViewScale
    {
        get => GetValue(ViewScaleProperty);
        set => SetValue(ViewScaleProperty, value);
    }
    public static readonly StyledProperty<float> ViewScaleProperty =
        AvaloniaProperty.Register<Spectrogram, float>(nameof(ViewScale), 120.0f);

    static Spectrogram()
    {
        AudioProperty.Changed.AddClassHandler<Spectrogram, IAudio>((x, e) => x.OnSpectrogramDependencyChanged());
        ViewScaleProperty.Changed.AddClassHandler<Spectrogram, float>((x, e) => x.OnSpectrogramDependencyChanged());
    }

    private void OnSpectrogramDependencyChanged()
    {
        Dispatcher.UIThread.InvokeAsync(RedrawSpectrogramVisuals);
    }

    public Spectrogram()
    {
        InitializeComponent();
    }

    private async Task RedrawSpectrogramVisuals()
    {
        if (Audio is not null)
        {
            await _drawingCts.CancelAsync();
            _drawingCts = new CancellationTokenSource();
            var audio = Audio;
            var viewScale = ViewScale;
            var channelId = SelectedAudioChannelId;
            Width = (int)(viewScale * audio.Length);
            
            var bmp = await Task.Run(() => DrawSpectrogramImage(audio, viewScale, channelId, _drawingCts.Token));
            if (bmp != null)
            {
                spectrogramImage.Source = bmp;
            }
        }
    }

    private static WriteableBitmap? DrawSpectrogramImage(IAudio audio, float viewScale, int channelId, CancellationToken cancellationToken = default)
    {
        const int displayStartFreq = 4;
        const int displayEndFreqMax = 20000;

        var displayEndFreq = Math.Min(displayEndFreqMax, audio.SampleRate * 0.95) / 2.0;

        int wres = (int)(viewScale * audio.Length);
        if (wres <= 0 || wres > 16384) return null;

        int hres = CavernHelper.defaultSamplingResolution / 2;
        
        var bitmap = new WriteableBitmap(new PixelSize(wres, hres), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var locked = bitmap.Lock())
        {
            unsafe
            {
                byte* ptr = (byte*)locked.Address;
                int stride = locked.RowBytes;

                for (int t = 0; t < wres; t++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    double t01 = t / (double)wres * audio.Length;
                    var spectrum = audio.GetLogSpectrum(channelId, t01, displayStartFreq, displayEndFreq);
                    
                    for (int f = 0; f < spectrum.Length && f < hres; f++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return null;

                        var s = Math.Pow(spectrum[f], 0.1);
                        double intensity = Math.Clamp(s, 0, 1);
                        int paletteIndex = (int)(intensity * (SpectrogramPalettes.Viridis.Length - 1));
                        var color = SpectrogramPalettes.Viridis[paletteIndex];

                        int row = spectrum.Length - 1 - f;
                        byte* pixelPtr = ptr + (row * stride) + (t * 4);
                        
                        pixelPtr[0] = color.B; // Blue
                        pixelPtr[1] = color.G; // Green
                        pixelPtr[2] = color.R; // Red
                        pixelPtr[3] = color.A; // Alpha
                    }
                }
            }
        }

        return bitmap;
    }
}