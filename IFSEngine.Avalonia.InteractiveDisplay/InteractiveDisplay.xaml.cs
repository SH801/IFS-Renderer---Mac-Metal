using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using IFSEngine.Rendering;
using AvaloniaVector = Avalonia.Vector;

namespace IFSEngine.Avalonia.InteractiveDisplay;

public partial class InteractiveDisplay : UserControl
{
    public RendererGL? Renderer { get; private set; }
    private WriteableBitmap? _writeableBitmap;

    public static readonly StyledProperty<ICommand?> InteractionStartedCommandProperty =
        AvaloniaProperty.Register<InteractiveDisplay, ICommand?>(nameof(InteractionStartedCommand));

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_writeableBitmap != null)
        {
            double srcWidth = _writeableBitmap.PixelSize.Width;
            double srcHeight = _writeableBitmap.PixelSize.Height;
            double destWidth = Bounds.Width;
            double destHeight = Bounds.Height;

            // Use Math.Max so the image scales up to fully cover the window bounds
            double scaleX = destWidth / srcWidth;
            double scaleY = destHeight / srcHeight;
            double scale = Math.Max(scaleX, scaleY);

            // Compute the scaled dimensions (one dimension will match the window, 
            // and the other will overflow and be cleanly cropped by the window edges)
            double renderWidth = srcWidth * scale;
            double renderHeight = srcHeight * scale;
            double renderX = (destWidth - renderWidth) / 2.0;
            double renderY = (destHeight - renderHeight) / 2.0;

            var sourceRect = new Rect(0, 0, srcWidth, srcHeight);
            var destRect = new Rect(renderX, renderY, renderWidth, renderHeight);

            // Draw the image, letting the overflow clip naturally at the control boundaries
            context.DrawImage(_writeableBitmap, sourceRect, destRect);
        }
        else
        {
            context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }
    }

    public ICommand? InteractionStartedCommand
    {
        get => GetValue(InteractionStartedCommandProperty);
        set => SetValue(InteractionStartedCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> InteractionFinishedCommandProperty =
        AvaloniaProperty.Register<InteractiveDisplay, ICommand?>(nameof(InteractionFinishedCommand));

    public ICommand? InteractionFinishedCommand
    {
        get => GetValue(InteractionFinishedCommandProperty);
        set => SetValue(InteractionFinishedCommandProperty, value);
    }

    public static readonly StyledProperty<bool> InvertRotationAxisYProperty =
        AvaloniaProperty.Register<InteractiveDisplay, bool>(nameof(InvertRotationAxisY), false);

    private bool _invertY = false;
    public bool InvertRotationAxisY
    {
        get => GetValue(InvertRotationAxisYProperty);
        set => SetValue(InvertRotationAxisYProperty, value);
    }

    public static readonly StyledProperty<float> SensitivityProperty =
        AvaloniaProperty.Register<InteractiveDisplay, float>(nameof(Sensitivity), 1.0f);

    private float _sensitivity = 1.0f;
    public float Sensitivity
    {
        get => GetValue(SensitivityProperty);
        set => SetValue(SensitivityProperty, value);
    }

    public event EventHandler<SizeChangedEventArgs>? DisplayResolutionChanged;

    private const float DeadZoneMultiplier = 1.0f;
    private const float ThumbstickValMax = 32768.0f;

    private bool IsInteractionEnabled => Renderer is not null && Renderer.IsRendering && Renderer.UpdateDisplayOnRender;

    private System.Timers.Timer? _controlTimer;
    private KeyboardHelper? _keyboard;
    private Vector2 _lastMousePos;
    private Vector2 _centerPivot;

    public InteractiveDisplay()
    {
        InitializeComponent();

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;

        // Wait until the control is attached to the view tree to find the parent Window/TopLevel
        Loaded += (s, e) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                _keyboard = new KeyboardHelper(topLevel);
                Console.WriteLine("[InteractiveDisplay] KeyboardHelper attached successfully.");
            }
            else
            {
                Console.WriteLine("[InteractiveDisplay] Error: Could not find parent TopLevel window.");
            }
        };
        _controlTimer = new System.Timers.Timer();
        _controlTimer.Elapsed += Control_Tick;
        _controlTimer.Interval = 16; // ms
        _controlTimer.Start();
    }

    public void AttachRenderer(RendererGL renderer)
    {
        Renderer = renderer;

        // Initialize resolution from current bounds
        UpdateDisplayResolution();

        // Handle window/control resizing dynamically
        this.SizeChanged += (s, e) =>
        {
            UpdateDisplayResolution();
        };

        // 1. Wire up the resolution change callback
        Renderer.OnResolutionChanged = (width, height) =>
        {
            UpdateBitmapSize(width, height);
        };

        Renderer.OnFrameReady += (bgra, bufferSize) =>
        {
            // 1. Immediately make a thread-safe copy of the payload synchronously 
            // so the render thread is free to loop without mutating our source data.
            byte[] bgraCopy = new byte[bufferSize];
            Buffer.BlockCopy(bgra, 0, bgraCopy, 0, (int)bufferSize);

            Dispatcher.UIThread.Post(() =>
            {
                if (_writeableBitmap == null)
                    return;

                using (var lockedFrame = _writeableBitmap.Lock())
                {
                    unsafe
                    {
                        fixed (byte* srcPtr = bgraCopy) // Use our safe copy!
                        {
                            byte* destPtr = (byte*)lockedFrame.Address;

                            int targetWidth = Renderer.HistogramWidth;
                            int targetHeight = Renderer.HistogramHeight;

                            int srcRowBytes = targetWidth * 4;

                            int rowBytesToCopy = Math.Min(
                                srcRowBytes,
                                lockedFrame.RowBytes);

                            int rowsToCopy = Math.Min(
                                targetHeight,
                                _writeableBitmap.PixelSize.Height);

                            for (int y = 0; y < rowsToCopy; y++)
                            {
                                Buffer.MemoryCopy(
                                    srcPtr + (y * srcRowBytes),
                                    destPtr + (y * lockedFrame.RowBytes),
                                    lockedFrame.RowBytes,
                                    rowBytesToCopy);
                            }
                        }
                    }
                }

                InvalidateVisual();
            });
        };
    }

    private void UpdateDisplayResolution()
    {
        if (Renderer == null) return;

        int width = (int)Math.Max(1, Bounds.Width);
        int height = (int)Math.Max(1, Bounds.Height);

        // 1. Tell renderer to update its internal target resolution
        Renderer.SetDisplayResolution(width, height);
    }

    public void UpdateBitmapSize(int width, int height)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Only recreate if dimensions actually changed or it doesn't exist yet
            if (_writeableBitmap == null || _writeableBitmap.PixelSize.Width != width || _writeableBitmap.PixelSize.Height != height)
            {
                var oldBitmap = _writeableBitmap;
                var newBitmap =  new WriteableBitmap(
                                    new PixelSize(width, height),
                                    new AvaloniaVector(96, 96),
                                    PixelFormat.Bgra8888,
                                    AlphaFormat.Premul); 
                _writeableBitmap = newBitmap;
                oldBitmap?.Dispose();
                
                Console.WriteLine($"[InteractiveDisplay] WriteableBitmap resized to {width}x{height}");
            }
        });
    }

    private void Control_Tick(object? sender, EventArgs e)
    {
        if (_keyboard == null || !IsInteractionEnabled || Renderer is null) 
            return;

        //TODO: InteractionStartedCommand?.Execute(null);
        Vector3 translateVec = new();
        Vector3 rotateVec = new();
        float fdDelta = 0;

        //keyboard input
        translateVec += new Vector3(
            (_keyboard.IsKeyDown(Key.D) ? 1 : 0) - (_keyboard.IsKeyDown(Key.A) ? 1 : 0),
            (_keyboard.IsKeyDown(Key.E) ? 1 : 0) - (_keyboard.IsKeyDown(Key.Q) ? 1 : 0),
            (_keyboard.IsKeyDown(Key.W) ? 1 : 0) - (_keyboard.IsKeyDown(Key.S) ? 1 : 0)) * 0.005f;
        rotateVec += new Vector3(
            (_keyboard.IsKeyDown(Key.L) ? 1 : 0) - (_keyboard.IsKeyDown(Key.J) ? 1 : 0),
            (_keyboard.IsKeyDown(Key.K) ? 1 : 0) - (_keyboard.IsKeyDown(Key.I) ? 1 : 0),
            (_keyboard.IsKeyDown(Key.U) ? 1 : 0) - (_keyboard.IsKeyDown(Key.O) ? 1 : 0)) * 0.03f;
        //keyboard sensitivity modifiers:
        if (_keyboard.IsKeyDown(Key.LeftCtrl) || _keyboard.IsKeyDown(Key.RightCtrl))
        {
            translateVec *= 2.0f;
            rotateVec *= 2.0f;
        }
        if (_keyboard.IsKeyDown(Key.LeftShift) || _keyboard.IsKeyDown(Key.RightShift))
        {
            translateVec *= 0.05f;
            rotateVec *= 0.05f;
        }

        if ((translateVec + rotateVec).Length() + fdDelta == 0.0f)
            return;

        //camera speed relates to focus distance
        float cameraSpeed = (float)Math.Abs(Renderer.LoadedParams.Camera.FocusDistance) * 2;
        translateVec *= cameraSpeed;

        if (_invertY)
            rotateVec.Y = -rotateVec.Y;

        //camera rotation speed depends on field of view
        float rotateSpeed = (float)Renderer.LoadedParams.Camera.FieldOfView / 180.0f;
        rotateVec = Vector3.Multiply(rotateVec, new Vector3(rotateSpeed, rotateSpeed, 1.0f));

        Renderer.LoadedParams.Camera.FocusDistance += fdDelta * Renderer.LoadedParams.Camera.FocusDistance * 0.03;
        Renderer.LoadedParams.Camera.Translate(translateVec * _sensitivity);
        Renderer.LoadedParams.Camera.Rotate(rotateVec * _sensitivity);
        Renderer.InvalidateHistogramBuffer();

        Dispatcher.InvokeAsync(() => InteractionFinishedCommand?.Execute(null), DispatcherPriority.Input);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        var pos = e.GetPosition(this);
        _lastMousePos = new Vector2((float)pos.X, (float)pos.Y);
        
        if (props.IsLeftButtonPressed)
        {
            InteractionStartedCommand?.Execute(null);
        }
    }

private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (IsInteractionEnabled && Renderer is not null)
        {
            var pos = e.GetPosition(this);
            var mousePos = new Vector2((float)pos.X, (float)pos.Y);
            var delta = mousePos - _lastMousePos;
            var props = e.GetCurrentPoint(this).Properties;

            if (props.IsLeftButtonPressed || props.IsRightButtonPressed || props.IsMiddleButtonPressed)
            {
                Vector3 rotateVec = Vector3.Zero;

                // Option: Create a bool InvertRotationAxisX = true; if you want it configurable, 
                // or just apply a negative sign directly to delta.X
                float horizontalMultiplier = -1.0f; // Flips left-right direction

                if (props.IsLeftButtonPressed)
                {
                    rotateVec = new(delta.X * horizontalMultiplier, delta.Y, 0.0f);
                }
                else if (props.IsRightButtonPressed)
                {
                    var lastCenterOffset = _lastMousePos - _centerPivot;
                    var lastDir = Vector2.Normalize(lastCenterOffset);
                    rotateVec = new(0.0f, 0.0f, lastDir.X * delta.Y - lastDir.Y * (delta.X * horizontalMultiplier));
                }
                else if (props.IsMiddleButtonPressed)
                {
                    rotateVec = new(delta.X * horizontalMultiplier, delta.Y, 0.0f);
                    if (rotateVec.Length() > 0.0f)
                    {
                        if (InvertRotationAxisY)
                            rotateVec.Y = -rotateVec.Y;

                        float rotateSpeed = (float)Renderer.LoadedParams.Camera.FieldOfView / 180.0f;
                        rotateVec.X *= rotateSpeed;
                        rotateVec.Y *= rotateSpeed;

                        var pivot = Renderer.LoadedParams.Camera.Position + Renderer.LoadedParams.Camera.ForwardDirection * (float)Renderer.LoadedParams.Camera.FocusDistance;
                        Renderer.LoadedParams.Camera.OrbitAround(pivot, rotateVec * 0.01f * Sensitivity);
                        Renderer.InvalidateHistogramBuffer();
                    }
                }

                if (rotateVec.Length() > 0.0f)
                {
                    if (InvertRotationAxisY)
                        rotateVec.Y = -rotateVec.Y;

                    float rotateSpeed = (float)Renderer.LoadedParams.Camera.FieldOfView / 180.0f;
                    rotateVec.X *= rotateSpeed;
                    rotateVec.Y *= rotateSpeed;

                    Renderer.LoadedParams.Camera.Rotate(rotateVec * 0.01f * Sensitivity);
                    Renderer.InvalidateHistogramBuffer();
                }
            }
            _lastMousePos = mousePos;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        InteractionFinishedCommand?.Execute(null);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsInteractionEnabled && Renderer is not null)
        {
            InteractionStartedCommand?.Execute(null);
            Renderer.LoadedParams.Camera.FocusDistance += (float)e.Delta.Y * Renderer.LoadedParams.Camera.FocusDistance * 0.1;
            Renderer.InvalidateHistogramBuffer();
            InteractionFinishedCommand?.Execute(null);
        }
    }

    private void UserControl_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = (int)Math.Max(1, Bounds.Width);
        var height = (int)Math.Max(1, Bounds.Height);
        Renderer?.SetDisplayResolution(width, height);
        _centerPivot = new Vector2((float)(Bounds.Width / 2.0f), (float)(Bounds.Height / 2.0f));
        DisplayResolutionChanged?.Invoke(this, e);
    }
}