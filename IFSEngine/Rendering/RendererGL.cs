#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

using IFSEngine.Model;
using IFSEngine.Rendering.GpuStructs;
using IFSEngine.Utility;

using Nito.AsyncEx;
using SharpMetal.Metal;
using SharpMetal.Foundation;


[SupportedOSPlatform("macos")]
public sealed class RendererGL : IAsyncDisposable
{
    /// <summary>
    /// Invoked by the rendering thread. The frequency of updates is influenced by <see cref="EnablePerceptualUpdates"/>.
    /// </summary>
    public event EventHandler? DisplayFramebufferUpdated;

    /// <summary>
    /// Invoked by the rendering thread when <see cref="TotalIterations"/> reaches the limit defined by <see cref="IFS.TargetIterationLevel"/>.
    /// </summary>
    public event EventHandler? TargetIterationReached;

    public bool IsInitialized { get; private set; } = false;
    public bool IsRendering { get; private set; } = false;

    public bool UpdateDisplayOnRender { get; set; } = true;

    /// <summary>
    /// Enable perceptually equal difference between updates.
    /// </summary>
    public bool EnablePerceptualUpdates { get; set; } = true;

    /// <summary>
    /// Enable Density Estimation.
    /// </summary>
    public bool EnableDE { get; set; } = false;
    public int DEMaxRadius { get; set; } = 9;
    public double DEPower { get; set; } = 0.2;
    public double DEThreshold { get; set; } = 0.4;

    /// <summary>
    /// Number of dispatches since accumulation reset.
    /// This is needed for random generation.
    /// </summary>
    private int _dispatchCnt = 0;

    public int HistogramWidth { get; private set; } = 1920;
    public int HistogramHeight { get; private set; } = 1080;
    public int DisplayWidth { get; private set; } = 1280;
    public int DisplayHeight { get; private set; } = 720;

    private string? _includesSource;
    private List<TransformPlugin>? _registeredTransforms;
    private List<EffectPlugin>? _registeredPostfxs;

    public IFS LoadedParams { get; private set; } = new IFS();
    private bool _invalidHistogramResolution = false;
    private bool _invalidHistogramBuffer = false;
    private bool _invalidParamsBuffer = false;
    private bool _invalidPointsStateBuffer = false;

    private MTLFunction _vertexFunction;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatchParametersStruct
    {
        public int width;
        public int height;
        public int reset_points_state;
        public int dispatch_cnt;
        public int invocation_iters;
        public uint seed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TonemapUniforms
    {
        public int width;
        public int height;
        public uint max_density;
        public float brightness;
        public float inv_gamma;
        public float gamma_threshold;
        public float vibrancy;
        // ADD THIS 4-BYTE PADDING:
        // Metal aligns float3 to 16 bytes, inserting 4 bytes of padding after vibrancy 
        // so that bg_color starts cleanly at byte offset 32.
        private float _padding;
        public Vector3 bg_color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEUniforms
    {
        public int width;
        public int height;
        public float de_max_radius;
        public float de_power;
        public float de_threshold;
        public uint max_density;
    }

    /// <summary>
    /// Number of workgroups to be dispatched. Each workgroup consists of 64 kernel invocations. Default value is 256.
    /// </summary>
    public int WorkgroupCount { get; private set; } = 256;
    public async Task SetWorkgroupCount(int s)
    {
        Console.WriteLine($"[RendererGL] SetWorkgroupCount called: {s}");
        WorkgroupCount = s;
        await WithContext(() =>
        {
            RecreatePointsBuffer();
            InvalidatePointsStateBuffer();
        });
    }

    private void RecreatePointsBuffer()
    {
        Console.WriteLine($"[RendererGL] RecreatePointsBuffer: InvocationCount={InvocationCount}");
        nuint pointsSize = (nuint)(InvocationCount * Marshal.SizeOf<PointStruct>());
        _pointsBuffer = _device.NewBuffer(pointsSize, MTLResourceOptions.ResourceStorageModePrivate);
    }

    private void InitShadersAndPipelines(IEnumerable<TransformPlugin> transforms, IEnumerable<EffectPlugin> postFxs)
    {
        Console.WriteLine("[RendererGL] InitShadersAndPipelines: Start");
        _defaultLibrary = _device.NewDefaultLibrary();
        if (_defaultLibrary == null)
        {
            throw new Exception("Failed to load default Metal library.");
        }
        
        // Initialize the compute kernel program with transforms
        InitComputeProgram(transforms);

        // Initialize the tonemap rendering pass
        InitTonemapPass();

        // Initialize Vertex shader
        InitVertexShader();

        // Initialize PostFx pipelines        
        InitPostFxPasses(postFxs);

        Console.WriteLine("[RendererGL] InitShadersAndPipelines: End");
    }

    private const int WorkgroupSize = 64; // Optimal is 64

    public int InvocationCount => WorkgroupCount * WorkgroupSize;

    /// <summary>
    /// Number of iterations a single invocation performs.
    /// </summary>
    public int InvocationIters { get; private set; } = 500;

    /// <summary>
    /// The render loop aims to reach the specified framerate.
    /// </summary>
    public int TargetFramerate { get; set; } = 60;

    /// <summary>
    /// Total iterations since accumulation reset
    /// </summary>
    public ulong TotalIterations { get; private set; } = 0;

    /// <summary>
    /// Maximum radius of the spatial filter.
    /// </summary>
    public int MaxFilterRadius { get; set; } = 0;
    public double HistogramScale { get; private set; } = 1.0;
    
    /// <summary>
    /// Seed value used to generate random numbers.
    /// </summary>
    public uint Seed { get; set; } = 0;
    
    public bool IsTargetIterationReached { get; private set; } = false;
    public bool MarkAreaInFocus { get; set; } = false;

    private bool _updateDisplayNow = false;

    // --- Metal Core Handles ---
    private readonly MTLDevice _device;
    private readonly MTLCommandQueue _commandQueue;
    private MTLLibrary? _defaultLibrary;

    // --- Metal Compute & Render Pipelines ---
    private MTLComputePipelineState _computePipelineState;
    private MTLRenderPipelineState _tonemapPipelineState;
    private MTLRenderPipelineState _dePipelineState;
    private readonly List<MTLRenderPipelineState> _postFxPipelineStates = [];

    // --- Metal GPU Buffers ---
    private MTLBuffer _histogramBuffer;
    private MTLBuffer _settingsBuffer;
    private MTLBuffer _iteratorsBuffer;
    private MTLBuffer _aliasBuffer;
    private MTLBuffer _pointsBuffer;
    private MTLBuffer _paletteBuffer;
    private MTLBuffer _realParametersBuffer;
    private MTLBuffer _vec3ParametersBuffer;

    // --- Metal Textures ---
    private MTLTexture _renderTexture;
    private MTLTexture _postFxTextureA;
    private MTLTexture _postFxTextureB;

    // --- Handle to InteractiveDisplay writeable bitmap ---
    public Action<int, int>? OnResolutionChanged { get; set; }
    public event Action<byte[], int>? OnFrameReady;

    private readonly AsyncAutoResetEvent _stopRender = new(false);

    private readonly float[] _bufferClearColor = [0.0f, 0.0f, 0.0f, 1.0f]; // RGBA
    private readonly string _shadersPath = "IFSEngine.Rendering.Shaders.";

    // --- Buffer Sizes ---
    private int _iteratorsBufferSize = 0;
    private int _realParametersBufferSize = 0;
    private int _vec3ParametersBufferSize = 0;
    private int _aliasBufferSize = 0;
    private int _paletteBufferSize = 0;

    public RendererGL(object? ctx = null)
    {
        Console.WriteLine("[RendererGL] Constructor: Initializing Metal device...");
        _device = MTLDevice.CreateSystemDefaultDevice();
        if (_device.NativePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Metal is not supported on this device or GPU initialization failed.");
        }

        _commandQueue = _device.NewCommandQueue();
        if (_commandQueue.NativePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create Metal command queue.");
        }

        _defaultLibrary = _device.NewDefaultLibrary();
        Console.WriteLine("[RendererGL] Constructor: Metal device and command queue initialized successfully.");
    }

    public async Task Initialize(IEnumerable<string> includeSources, IEnumerable<TransformPlugin> transforms, IEnumerable<EffectPlugin> postFxs)
    {
        Console.WriteLine("[RendererGL] Initialize: Start");
        if (IsInitialized)
            throw new InvalidOperationException("Renderer is already initialized.");

        _includesSource = string.Join(Environment.NewLine, includeSources);
        _registeredTransforms = transforms.ToList();
        _registeredPostfxs = postFxs.ToList();

        InitBuffers();
        InitShadersAndPipelines(_registeredTransforms, _registeredPostfxs);
        InitPostFxPingPongTextures();

        SetHistogramScaleToDisplay();

        IsInitialized = true;
        await SetWorkgroupCount(WorkgroupCount);

        InvalidateParamsBuffer();
        Console.WriteLine("[RendererGL] Initialize: End, IsInitialized = true");
    }

    public async Task LoadPlugins(IEnumerable<string> includeSources, IEnumerable<TransformPlugin> transforms, IEnumerable<EffectPlugin> postFxs)
    {
        Console.WriteLine("[RendererGL] LoadPlugins: Start");
        if (!IsInitialized)
            throw new InvalidOperationException("Renderer is not initialized.");

        await WithContext(async () =>
        {
            _includesSource = string.Join(Environment.NewLine, includeSources);
            _registeredTransforms = transforms.ToList();
            _registeredPostfxs = postFxs.ToList();

            InitComputeProgram(_registeredTransforms);
            InitPostFxPasses(_registeredPostfxs);

            InvalidateParamsBuffer();
            await Task.CompletedTask;
        });
        Console.WriteLine("[RendererGL] LoadPlugins: End");
    }

    public void LoadParams(IFS p)
    {
        Console.WriteLine("[RendererGL] LoadParams called");
        LoadedParams = p;
        InvocationIters = 500;
        InvalidateParamsBuffer();
        SetHistogramScaleToDisplay();
    }

    public void InvalidateParamsBuffer()
    {
        // Console.WriteLine("[RendererGL] InvalidateParamsBuffer triggered");
        InvalidateHistogramBuffer();
        _invalidParamsBuffer = true;
    }

    public void InvalidateHistogramBuffer()
    {
        // Console.WriteLine("[RendererGL] InvalidateHistogramBuffer triggered");
        InvalidatePointsStateBuffer();
        IsTargetIterationReached = false;
        _invalidHistogramBuffer = true;
    }

    public void InvalidateDisplay()
    {
        // Console.WriteLine("[RendererGL] InvalidateDisplay triggered");
        _updateDisplayNow = true;
    }

    private void InvalidatePointsStateBuffer()
    {
        // Console.WriteLine("[RendererGL] InvalidatePointsStateBuffer triggered");
        _invalidPointsStateBuffer = true;
    }

    public void SetHistogramScale(double scale)
    {
        // Console.WriteLine($"[RendererGL] SetHistogramScale: {scale}");
        HistogramScale = scale;
        int newWidth = (int)(LoadedParams.ImageResolution.Width * HistogramScale);
        int newHeight = (int)(LoadedParams.ImageResolution.Height * HistogramScale);
        if (newWidth != HistogramWidth || newHeight != HistogramHeight)
        {
            HistogramWidth = newWidth;
            HistogramHeight = newHeight;
            OnResolutionChanged?.Invoke(HistogramWidth, HistogramHeight);
            _invalidHistogramResolution = true;
            InvalidateHistogramBuffer();
        }
    }

    public void SetHistogramScaleToDisplay()
    {
        double rw = DisplayWidth / (double)LoadedParams.ImageResolution.Width;
        double rh = DisplayHeight / (double)LoadedParams.ImageResolution.Height;
        double rr = Math.Min(rw, rh) * .98;
        // Console.WriteLine($"[RendererGL] SetHistogramScaleToDisplay calculated scale: {rr}");
        SetHistogramScale(rr);
    }

    private void UpdateHistogramResolution()
    {
        Console.WriteLine($"[RendererGL] UpdateHistogramResolution: Width={HistogramWidth}, Height={HistogramHeight}");

        nuint histogramByteSize = (nuint)(HistogramWidth * HistogramHeight * 4 * sizeof(float));
        if (_histogramBuffer.Contents != IntPtr.Zero) _histogramBuffer.Dispose();
        _histogramBuffer = _device.NewBuffer(histogramByteSize, MTLResourceOptions.ResourceStorageModeShared);

        var descriptor = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.RGBA32Float, (nuint)HistogramWidth, (nuint)HistogramHeight, false);
        descriptor.Usage = MTLTextureUsage.ShaderRead | MTLTextureUsage.ShaderWrite | MTLTextureUsage.RenderTarget;
        descriptor.StorageMode = MTLStorageMode.Shared;

        if (_postFxTextureA.NativePtr != IntPtr.Zero) _postFxTextureA.Dispose();
        _postFxTextureA = _device.NewTexture(descriptor);

        if (_postFxTextureB.NativePtr != IntPtr.Zero) _postFxTextureB.Dispose();
        _postFxTextureB = _device.NewTexture(descriptor);

        if (_renderTexture.NativePtr != IntPtr.Zero) _renderTexture.Dispose();
        _renderTexture = _device.NewTexture(descriptor);

        DisplayFramebufferUpdated?.Invoke(this, EventArgs.Empty);

        _invalidHistogramResolution = false;
    }

    public void SetDisplayResolution(int displayWidth, int displayHeight)
    {
        Console.WriteLine($"[RendererGL] SetDisplayResolution: {displayWidth}x{displayHeight}");
        DisplayWidth = displayWidth;
        DisplayHeight = displayHeight;
        InvalidateDisplay();
    }

    private void UpdateBufferData<T>(ref MTLBuffer buffer, T[] data, ref int currentSize, int newCount) where T : struct
    {
        int elementSize = Marshal.SizeOf<T>();
        nuint byteSize = (nuint)(newCount * elementSize);

        if (newCount == currentSize && buffer is MTLBuffer buf)
        {
            IntPtr ptr = buf.Contents;

            if (data.Length > 0)
            {
                ReadOnlySpan<byte> byteSpan = MemoryMarshal.AsBytes(data.AsSpan(0, newCount));
                unsafe
                {
                    fixed (byte* srcPtr = byteSpan)
                    {
                        Buffer.MemoryCopy(srcPtr, (void*)ptr, byteSpan.Length, byteSpan.Length);
                    }
                }
            }
        }
        else
        {
            currentSize = newCount;
            if (buffer.Contents != IntPtr.Zero) buffer.Dispose();
            buffer = _device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeShared);

            if (buffer is MTLBuffer newBuf && data.Length > 0)
            {
                IntPtr ptr = newBuf.Contents;
                
                ReadOnlySpan<byte> byteSpan = MemoryMarshal.AsBytes(data.AsSpan(0, newCount));
                unsafe
                {
                    fixed (byte* srcPtr = byteSpan)
                    {
                        Buffer.MemoryCopy(srcPtr, (void*)ptr, byteSpan.Length, byteSpan.Length);
                    }
                }
            }
        }
    }

    public unsafe void RenderImage()
    {
        Console.WriteLine($"[RendererGL] RenderImage: Start (TotalIterations={TotalIterations})");

        if (!IsInitialized)
            throw new InvalidOperationException("Renderer is not initialized.");

        if (_renderTexture.NativePtr != IntPtr.Zero)
        {
            var renderPassDesc = new MTLRenderPassDescriptor();
            var colorAttachment = renderPassDesc.ColorAttachments.Object(0UL);
            colorAttachment.Texture = _renderTexture;
            colorAttachment.LoadAction = MTLLoadAction.Clear;
            colorAttachment.StoreAction = MTLStoreAction.Store;
            colorAttachment.ClearColor = new MTLClearColor 
            { 
                red = _bufferClearColor[0], 
                green = _bufferClearColor[1], 
                blue = _bufferClearColor[2], 
                alpha = _bufferClearColor[3] 
            };

            var commandBuffer = _commandQueue.CommandBuffer();
            var renderEncoder = commandBuffer.RenderCommandEncoder(renderPassDesc);

            // --- 1. Tonemap Pass ---
            if ((_tonemapPipelineState.NativePtr != IntPtr.Zero) && (_histogramBuffer.Contents != IntPtr.Zero))
            {
                renderEncoder.SetRenderPipelineState(_tonemapPipelineState);
                renderEncoder.SetFragmentBuffer(_histogramBuffer, 0, 1);

                float maxDensity = 1.0f + (float)(TotalIterations / (ulong)(HistogramWidth * HistogramHeight));

                var tonemapParams = new TonemapUniforms
                {
                    width = HistogramWidth,
                    height = HistogramHeight,
                    max_density = (uint)maxDensity,
                    brightness = (float)LoadedParams.Brightness,
                    inv_gamma = (float)(1.0f / LoadedParams.Gamma),
                    gamma_threshold = (float)LoadedParams.GammaThreshold,
                    vibrancy = (float)LoadedParams.Vibrancy,
                    bg_color = new Vector3(LoadedParams.BackgroundColor.R / 255.0f, LoadedParams.BackgroundColor.G / 255.0f, LoadedParams.BackgroundColor.B / 255.0f)
                };

                GCHandle handle = GCHandle.Alloc(tonemapParams, GCHandleType.Pinned);
                try
                {
                    renderEncoder.SetFragmentBytes(handle.AddrOfPinnedObject(), (nuint)Marshal.SizeOf<TonemapUniforms>(), 0);
                }
                finally
                {
                    handle.Free();
                }

                renderEncoder.DrawPrimitives(MTLPrimitiveType.TriangleStrip, 0, 4);
            }
            else
            {
                Console.WriteLine("[RendererGL] RenderImage Warning: Tonemap pipeline or histogram buffer is missing!");
            }

            // --- 2. Density Estimation (DE) Pass ---
            if (EnableDE && (_dePipelineState.NativePtr != IntPtr.Zero))
            {
                renderEncoder.SetRenderPipelineState(_dePipelineState);

                float maxDensity = 1.0f + (float)(TotalIterations / (ulong)(HistogramWidth * HistogramHeight));
                var deParams = new DEUniforms
                {
                    width = HistogramWidth,
                    height = HistogramHeight,
                    de_max_radius = (float)DEMaxRadius,
                    de_power = (float)DEPower,
                    de_threshold = (float)DEThreshold,
                    max_density = (uint)maxDensity
                };

                GCHandle handle = GCHandle.Alloc(deParams, GCHandleType.Pinned);
                try
                {
                    renderEncoder.SetFragmentBytes(handle.AddrOfPinnedObject(), (nuint)Marshal.SizeOf<DEUniforms>(), 0);
                }
                finally
                {
                    handle.Free();
                }

                renderEncoder.DrawPrimitives(MTLPrimitiveType.TriangleStrip, 0, 4);
            }

            renderEncoder.EndEncoding();

            // --- 3. Post-Processing Chain ---
            ApplyPostFxPasses(commandBuffer);

            // Wait for the render passes and post-fx to finish before reading back from GPU texture
            commandBuffer.Commit();
            commandBuffer.WaitUntilCompleted();
        }
        else
        {
            Console.WriteLine("[RendererGL] RenderImage Warning: _renderTexture has no value!");
        }

        Console.WriteLine("[RendererGL] RenderImage: End");
    }
    
    public static byte[] ConvertToBgra(float[,,] pixels)
    {
        int height = pixels.GetLength(0);
        int width = pixels.GetLength(1);

        byte[] result = new byte[width * height * 4];

        int i = 0;

        static byte ToByte(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0;

            value = Math.Clamp(value, 0f, 1f);
            return (byte)MathF.Round(value * 255f);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float r = pixels[y, x, 0];
                float g = pixels[y, x, 1];
                float b = pixels[y, x, 2];
                float a = pixels[y, x, 3];

                result[i++] = ToByte(b); // Blue
                result[i++] = ToByte(g); // Green
                result[i++] = ToByte(r); // Red
                result[i++] = ToByte(a); // Alpha
            }
        }

        return result;
    }

    private async void BlitToDisplayFramebuffer()
    {
        Console.WriteLine("[RendererGL] BlitToDisplayFramebuffer (Readback) invoked");

        if (_renderTexture.NativePtr == IntPtr.Zero)
        {
            return;
        }

        float[,,] pixels = await ReadPixelData();
        byte[] bgra = ConvertToBgra(pixels);

        OnFrameReady?.Invoke(bgra, bgra.Length);
    }

    private void AdjustWorkloadSize(double elapsedMilliseconds)
    {
        int actualMs = (int)elapsedMilliseconds;
        int targetMs = 1000 / TargetFramerate;
        double adjustment = (actualMs > targetMs) ? 0.66 : 1.1;
        int adjustedInvocationIters = (int)(InvocationIters * adjustment);
        InvocationIters = Math.Clamp(adjustedInvocationIters, 50, 20000);
    }

    public unsafe void DispatchCompute()
    {
        // Console.WriteLine("[RendererGL] DispatchCompute: Start");

        if (_computePipelineState.NativePtr == IntPtr.Zero)
        {
            Console.WriteLine("[RendererGL] DispatchCompute Warning: _computePipelineState is null!");
            return;
        }

        if (!IsInitialized)
            throw new InvalidOperationException("Renderer is not initialized.");

        var commandBuffer = _commandQueue.CommandBuffer();

        var globalsBuffer = _device.NewBuffer((nuint)Marshal.SizeOf<GlobalsStruct>(), 0);
        var globalsPtr = (GlobalsStruct*)globalsBuffer.Contents.ToPointer();
        *globalsPtr = new GlobalsStruct 
        {
            seed = Seed,
            width = HistogramWidth,
            height = HistogramHeight,
            dispatch_cnt = _dispatchCnt,
            reset_points_state = _invalidPointsStateBuffer ? 1 : 0,
            invocation_iters = InvocationIters  
        };

        // 1. Handle Histogram / Resolution Invalidation & Clearing
        if (_invalidHistogramBuffer)
        {
            Console.WriteLine("[RendererGL] DispatchCompute: Resetting histogram buffer due to invalidation.");
            if (_invalidHistogramResolution)
            {
                UpdateHistogramResolution();
            }

            nuint histogramSize = (nuint)(HistogramWidth * HistogramHeight * 4 * sizeof(float));
            System.Runtime.CompilerServices.Unsafe.InitBlock(_histogramBuffer.Contents.ToPointer(), 0, (uint)histogramSize);

            _invalidHistogramBuffer = false;
            _dispatchCnt = 0;
            TotalIterations = 0;
            InvalidateDisplay();
            InvalidatePointsStateBuffer();

            var settings = new SettingsStruct
            {
                camera_params = LoadedParams.Camera.GetCameraParameters(),
                itnum = LoadedParams.Iterators.Count,
                fog_effect = (float)LoadedParams.FogEffect,
                palettecnt = LoadedParams.Palette.Colors.Count,
                entropy = (float)LoadedParams.Entropy,
                warmup = LoadedParams.Warmup,
                max_filter_radius = MaxFilterRadius,
                mark_area_in_focus = MarkAreaInFocus ? 1 : 0
            };

            if (_settingsBuffer.Contents != IntPtr.Zero)
            {
                Marshal.StructureToPtr(settings, _settingsBuffer.Contents, false);
            }
        }

        // 2. Handle Parameter Updates & Buffer Uploads
        if (_invalidParamsBuffer)
        {
            Console.WriteLine("[RendererGL] DispatchCompute: Updating parameter buffers.");
            var its = new List<IteratorStruct>();
            var allRealParams = new List<float>();
            var allVec3Params = new List<Vector4>();
            var currentIterators = LoadedParams.Iterators.ToList();

            double sumStartWeights = currentIterators.Sum(i => i.StartWeight);
            if (sumStartWeights == 0.0)
            {
                Console.WriteLine("[RendererGL] DispatchCompute: Warning - sumStartWeights is 0.");
                return;
            }
            var normalizedStartWeights = currentIterators.Select(i => i.StartWeight / sumStartWeights).ToList();
            var aliasTables = AliasMethod.GenerateAliasTable(normalizedStartWeights).ToList();
            for (int iti = 0; iti < currentIterators.Count; iti++)
            {
                var it = currentIterators[iti];
                its.Add(new IteratorStruct
                {
                    tfId = _registeredTransforms?.IndexOf(it.Transform) ?? 0,
                    real_params_index = allRealParams.Count,
                    vec3_params_index = allVec3Params.Count,
                    color_speed = (float)it.ColorSpeed,
                    color_index = (float)it.ColorIndex,
                    opacity = (float)it.Opacity,
                    shading_mode = (int)it.ShadingMode,
                    tf_mix = (float)it.Mix,
                    tf_add = (float)it.Add,
                    reset_alias = aliasTables[iti].k,
                    reset_prob = (float)aliasTables[iti].u
                });
                allRealParams.AddRange(it.RealParams.Values.Select(p => (float)p).ToList());
                allVec3Params.AddRange(it.Vec3Params.Values.Select(p => new Vector4(p, 0.0f)).ToList());
            }

            UpdateBufferData(ref _iteratorsBuffer, its.ToArray(), ref _iteratorsBufferSize, its.Count);
            UpdateBufferData(ref _realParametersBuffer, allRealParams.Select(f => new Vector4(f)).ToArray(), ref _realParametersBufferSize, allRealParams.Count);
            UpdateBufferData(ref _vec3ParametersBuffer, allVec3Params.ToArray(), ref _vec3ParametersBufferSize, allVec3Params.Count);

            double SumWeights = currentIterators.Sum(i => i.BaseWeight);
            var normalizedBaseWeights = currentIterators.ToDictionary(i => i, i => i.BaseWeight / SumWeights);
            var xaosAliasTables = new List<(double u, int k)>();
            foreach (var it in currentIterators)
            {
                var itWeights = new List<double>(currentIterators.Count);
                foreach (var toIt in currentIterators)
                {
                    if (it.WeightTo.TryGetValue(toIt, out var value))
                        itWeights.Add(value * normalizedBaseWeights[toIt]);
                    else
                        itWeights.Add(0);
                }
                double sumw = itWeights.Sum();
                if (sumw > 0)
                {
                    itWeights = itWeights.Select(w => w / sumw).ToList();
                    xaosAliasTables.AddRange(AliasMethod.GenerateAliasTable(itWeights));
                }
                else
                {
                    xaosAliasTables.AddRange(Enumerable.Repeat((-1.0, -1), currentIterators.Count));
                }
            }

            var xaosData = xaosAliasTables.Select(t => new Vector4((float)t.u, t.k, 0f, 0f)).ToArray();
            UpdateBufferData(ref _aliasBuffer, xaosData, ref _aliasBufferSize, currentIterators.Count * currentIterators.Count);

            var paletteData = LoadedParams.Palette.Colors.ToArray();
            UpdateBufferData(ref _paletteBuffer, paletteData, ref _paletteBufferSize, LoadedParams.Palette.Colors.Count);

            _invalidParamsBuffer = false;
        }

        // 3. Encode and Dispatch Compute Pass
        var computeEncoder = commandBuffer.ComputeCommandEncoder();
        computeEncoder.SetComputePipelineState(_computePipelineState);
        computeEncoder.SetBuffer(globalsBuffer,             0, 0);
        computeEncoder.SetBuffer(_settingsBuffer,           0, 1);
        computeEncoder.SetBuffer(_paletteBuffer,            0, 2);
        computeEncoder.SetBuffer(_iteratorsBuffer,          0, 3);
        computeEncoder.SetBuffer(_aliasBuffer,              0, 4);
        computeEncoder.SetBuffer(_histogramBuffer,          0, 5);
        computeEncoder.SetBuffer(_pointsBuffer,             0, 6);
        computeEncoder.SetBuffer(_realParametersBuffer,     0, 7);
        computeEncoder.SetBuffer(_vec3ParametersBuffer,     0, 8);

        var threadsPerThreadgroup = new MTLSize { width = (ulong)WorkgroupSize, height = 1, depth = 1 };
        var threadgroupsPerGrid = new MTLSize 
        { 
            width = ((ulong)InvocationCount + (ulong)WorkgroupSize - 1) / (ulong)WorkgroupSize, 
            height = 1, 
            depth = 1 
        };

        computeEncoder.DispatchThreadgroups(threadgroupsPerGrid, threadsPerThreadgroup);
        computeEncoder.DispatchThreadgroups(threadgroupsPerGrid, threadsPerThreadgroup);
        computeEncoder.EndEncoding();

        commandBuffer.Commit();
        commandBuffer.WaitUntilCompleted();

        // 4. Update Iteration Metrics & Counters
        _invalidPointsStateBuffer = false;
        TotalIterations += Convert.ToUInt64(InvocationIters * InvocationCount);
        _dispatchCnt++;

        // Console.WriteLine("[RendererGL] DispatchCompute: End");
    }

    public void StartRenderLoop()
    {
        
        Console.WriteLine("[RendererGL] StartRenderLoop: Start");

        if (!IsInitialized)
            throw new InvalidOperationException("Renderer is not initialized.");

        if (!IsRendering)
        {
            IsRendering = true;

            ThreadPool.QueueUserWorkItem((_) =>
            {
                Console.WriteLine("[RendererGL] RenderLoop Thread Started");
                while (IsRendering)
                {
                    // Record start time and complete command buffer to measure duration
                    var startTime = DateTime.UtcNow;

                    // Enqueue compute pass
                    DispatchCompute();

                    var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                    AdjustWorkloadSize(elapsedMs);

                    bool target = BitOperations.Log2(1 + TotalIterations / (ulong)(HistogramWidth * HistogramHeight)) >= LoadedParams.TargetIterationLevel;
                    bool isTargetIterationReachedByCurrentDispatch = !IsTargetIterationReached && target;
                    IsTargetIterationReached = target;

                    bool isPerceptuallyEqualFrame = BitOperations.IsPow2(_dispatchCnt);

                    if (_updateDisplayNow || isTargetIterationReachedByCurrentDispatch || (UpdateDisplayOnRender && (!EnablePerceptualUpdates || (EnablePerceptualUpdates && isPerceptuallyEqualFrame))))
                    {
                        Console.WriteLine($"[RendererGL] RenderLoop: Triggering frame render (TotalIterations={TotalIterations})");
                        // Render image from histogram and blit to display
                        RenderImage();
                        BlitToDisplayFramebuffer();
                        
                        _updateDisplayNow = false;
                        DisplayFramebufferUpdated?.Invoke(this, EventArgs.Empty);
                    }

                    if (isTargetIterationReachedByCurrentDispatch)
                    {
                        Console.WriteLine("[RendererGL] RenderLoop: Target iteration level reached!");
                        TargetIterationReached?.Invoke(this, EventArgs.Empty);
                    }
                }
                
                _stopRender.Set();
                Console.WriteLine("[RendererGL] RenderLoop Thread Stopped");
            });
        }

        Console.WriteLine("[RendererGL] StartRenderLoop: End");
    }

    /// <summary>
    /// Wait for the render thread to stop.
    /// </summary>
    public async Task StopRenderLoop()
    {
        Console.WriteLine("[RendererGL] StopRenderLoop called");
        if (!IsRendering)
            throw new InvalidOperationException("The render loop is not running.");
        IsRendering = false;
        await _stopRender.WaitAsync();
        Console.WriteLine("[RendererGL] StopRenderLoop completed");
    }

    /// <summary>
    /// Helper to execute tasks safely. Pauses and resumes the render thread if it is active.
    /// </summary>
    private async Task WithContext(Action action)
    {
        bool renderingPaused = IsRendering;
        if (renderingPaused)
        {
            Console.WriteLine("[RendererGL] WithContext: Pausing render loop...");
            await StopRenderLoop();
        }
        
        action();
        
        if (renderingPaused)
        {
            Console.WriteLine("[RendererGL] WithContext: Resuming render loop...");
            StartRenderLoop();
        }
    }

    /// <summary>
    /// Writes the pixel data to the specified pointer.
    /// The format is ubyte, bgra, which is ideal for filling a bitmap buffer quickly.
    /// </summary>
    public async Task CopyPixelDataToBitmap(IntPtr ptr)
    {
        Console.WriteLine("[RendererGL] CopyPixelDataToBitmap invoked");

        // 1. Get the float data via your existing Metal-enabled method
        float[,,] pixels = await ReadPixelData();

        // 2. Convert the float pixel array into BGRA bytes
        byte[] bgra = ConvertToBgra(pixels);

        // 3. Copy the final byte array directly into the target bitmap pointer
        if (ptr != IntPtr.Zero && bgra.Length > 0)
        {
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, ptr, bgra.Length);
        }

        Console.WriteLine("[RendererGL] CopyPixelDataToBitmap finished");
    }

    /// <summary>
    /// Format: float[y, x, rgba]
    /// </summary>
    public async Task<float[,,]> ReadPixelData()
    {
        Console.WriteLine("[RendererGL] ReadPixelData invoked");
        if (!IsInitialized)
            throw new InvalidOperationException("Renderer is not initialized.");

        InvalidateDisplay();
        float[,,] o = new float[HistogramHeight, HistogramWidth, 4];
        
        await WithContext(() =>
        {
            if (_renderTexture.NativePtr == IntPtr.Zero)
            {
                Console.WriteLine("[RendererGL] ReadPixelData Warning: _renderTexture has no value.");
                return;
            }

            ulong bytesPerRow = (ulong)(HistogramWidth * 4 * sizeof(float));
            ulong totalByteSize = bytesPerRow * (ulong)HistogramHeight;

            var stagingBuffer = _device.NewBuffer((nuint)totalByteSize, MTLResourceOptions.ResourceStorageModeShared);
            var commandBuffer = _commandQueue.CommandBuffer();
            var blitEncoder = commandBuffer.BlitCommandEncoder();
            
            blitEncoder.CopyFromTexture(
                _renderTexture,
                0UL,
                0UL,
                new MTLOrigin { x = 0, y = 0, z = 0 },
                new MTLSize { width = (ulong)HistogramWidth, height = (ulong)HistogramHeight, depth = 1 },
                stagingBuffer,
                0UL,
                bytesPerRow,
                totalByteSize
            );

            blitEncoder.EndEncoding();
            commandBuffer.Commit();
            commandBuffer.WaitUntilCompleted();

            IntPtr bufferPtr = stagingBuffer.Contents;
            if (bufferPtr != IntPtr.Zero)
            {
                int totalFloats = HistogramWidth * HistogramHeight * 4;
                float[] flatData = new float[totalFloats];
                Marshal.Copy(bufferPtr, flatData, 0, totalFloats);

                int index = 0;
                for (int y = 0; y < HistogramHeight; y++)
                {
                    for (int x = 0; x < HistogramWidth; x++)
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            o[y, x, c] = flatData[index++];
                        }
                    }
                }
            }

            stagingBuffer.Dispose();
        });

        Console.WriteLine("[RendererGL] ReadPixelData finished");
        return o;
    }

    public async Task<float[][][]> ReadHistogramData()
    {
        Console.WriteLine("[RendererGL] ReadHistogramData invoked");
        if (!IsInitialized)
            throw new InvalidOperationException("Renderer is not initialized.");

        InvalidateDisplay();
        float[] o2 = new float[HistogramWidth * HistogramHeight * 4];
        
        await WithContext(() =>
        {
            if (_histogramBuffer.Contents == IntPtr.Zero)
            {
                Console.WriteLine("[RendererGL] ReadHistogramData Warning: _histogramBuffer has no value.");
                return;
            }

            ulong byteSize = (ulong)(HistogramWidth * HistogramHeight * 4 * sizeof(float));

            var stagingBuffer = _device.NewBuffer((nuint)byteSize, MTLResourceOptions.ResourceStorageModeShared);

            var commandBuffer = _commandQueue.CommandBuffer();
            var blitEncoder = commandBuffer.BlitCommandEncoder();
            
            blitEncoder.CopyFromBuffer(
                _histogramBuffer, 
                0UL, 
                stagingBuffer, 
                0UL, 
                byteSize
            );
            
            blitEncoder.EndEncoding();
            commandBuffer.Commit();
            commandBuffer.WaitUntilCompleted();

            IntPtr bufferPtr = stagingBuffer.Contents;
            if (bufferPtr != IntPtr.Zero)
            {
                Marshal.Copy(bufferPtr, o2, 0, o2.Length);
            }

            stagingBuffer.Dispose();
        });

        var totalPixels = (double)(HistogramWidth * HistogramHeight);
        var div = LoadedParams.Brightness * (1.0 + ((double)TotalIterations / totalPixels));

        // var div = LoadedParams.Brightness * (1.0 + (TotalIterations / (ulong)(HistogramWidth * HistogramHeight)));
        for (int i = 0; i < o2.Length; i++)
            o2[i] = (float)(o2[i] / div);

        var histogram = o2
            .Chunk(4)
            .Chunk(HistogramWidth)
            .Chunk(HistogramHeight)
            .First().ToArray();

        Console.WriteLine("[RendererGL] ReadHistogramData finished");
        return histogram;
    }

    private void InitVertexShader()
    {
        Console.WriteLine("[RendererGL] InitVertexShader: Start");
        var assembly = typeof(RendererGL).GetTypeInfo().Assembly;

        var resource = assembly.GetManifestResourceStream(_shadersPath + "quad.vert.metal");
        if (resource == null)
        {
            throw new FileNotFoundException("Shader resource not found: quad.vert.metal");
        }
        string vertSource = new StreamReader(resource).ReadToEnd();

        NSError vertError = default;
        var vertLibrary = _device.NewLibrary(vertSource, new MTLCompileOptions(), ref vertError);
        
        _vertexFunction = vertLibrary.NewFunction("main0");
        if (_vertexFunction.NativePtr == IntPtr.Zero)
        {
            throw new Exception(string.Format("Error compiling Vertex shader: {0}", vertError.LocalizedDescription));
        }
        Console.WriteLine("[RendererGL] InitVertexShader: End");
    }

    private void InitTonemapPass()
    {
        Console.WriteLine("[RendererGL] InitTonemapPass: Start");
        var assembly = typeof(RendererGL).GetTypeInfo().Assembly;

        var fragResource = assembly.GetManifestResourceStream(_shadersPath + "tonemap.frag.metal");
        if (fragResource == null)
        {
            throw new FileNotFoundException("Shader resource not found: tonemap.frag.metal");
        }
        string fragSource = new StreamReader(fragResource).ReadToEnd();
        fragSource = fragSource.Replace("@includes", _includesSource);

        NSError fragError = default;
        var fragLibrary = _device.NewLibrary(fragSource, new MTLCompileOptions(), ref fragError);
        var fragFunc = fragLibrary.NewFunction("main0");
        if (fragFunc.NativePtr == IntPtr.Zero)
        {
            throw new Exception(string.Format("Error compiling Fragment shader: {0}", fragError.LocalizedDescription));
        }

        var vertResource = assembly.GetManifestResourceStream(_shadersPath + "quad.vert.metal");
        if (vertResource == null)
        {
            throw new FileNotFoundException("Shader resource not found: quad.vert.metal");
        }
        string vertSource = new StreamReader(vertResource).ReadToEnd();

        NSError vertError = default;
        var vertLibrary = _device.NewLibrary(vertSource, new MTLCompileOptions(), ref vertError);
        var vertFunc = vertLibrary.NewFunction("main0");
        if (vertFunc.NativePtr == IntPtr.Zero)
        {
            throw new Exception(string.Format("Error compiling Vertex shader: {0}", vertError.LocalizedDescription));
        }

        var textureDesc = MTLTextureDescriptor.Texture2DDescriptor(
            MTLPixelFormat.RGBA32Float, 
            (nuint)HistogramWidth, 
            (nuint)HistogramHeight, 
            false
        );
        textureDesc.Usage = MTLTextureUsage.ShaderRead | MTLTextureUsage.ShaderWrite | MTLTextureUsage.RenderTarget;
        textureDesc.StorageMode = MTLStorageMode.Shared;

        _renderTexture = _device.NewTexture(textureDesc);

        var pipelineDesc = new MTLRenderPipelineDescriptor();
        pipelineDesc.VertexFunction = vertFunc;
        pipelineDesc.FragmentFunction = fragFunc;
        
        var colorAttachment = pipelineDesc.ColorAttachments.Object(0);
        colorAttachment.PixelFormat = MTLPixelFormat.RGBA32Float;
        pipelineDesc.ColorAttachments.SetObject(colorAttachment, 0);

        NSError psoError = default;
        _tonemapPipelineState = _device.NewRenderPipelineState(pipelineDesc, ref psoError);
        if (_tonemapPipelineState.NativePtr == IntPtr.Zero)
        {
            throw new Exception(string.Format("Error linking program/pipeline state: {0}", psoError.LocalizedDescription));
        }
        Console.WriteLine("[RendererGL] InitTonemapPass: End");
    }

    private void InitDEPass()
    {
        Console.WriteLine("[RendererGL] InitDEPass: Start");
        var assembly = typeof(RendererGL).GetTypeInfo().Assembly;

        var resource = assembly.GetManifestResourceStream(_shadersPath + "de.frag.metal");
        if (resource == null)
        {
            throw new FileNotFoundException("Shader resource not found: de.frag.metal");
        }
        string deShaderSource = new StreamReader(resource).ReadToEnd();

        NSError deError = default;
        var deLibrary = _device.NewLibrary(deShaderSource, new MTLCompileOptions(), ref deError);
        var fragFunc = deLibrary.NewFunction("main0");
        if (fragFunc.NativePtr == IntPtr.Zero)
        {
            throw new Exception(string.Format("Error compiling DE fragment shader: {0}", deError.LocalizedDescription));
        }

        var pipelineDesc = new MTLRenderPipelineDescriptor();
        pipelineDesc.VertexFunction = _vertexFunction; 
        pipelineDesc.FragmentFunction = fragFunc;
        
        var colorAttachment = pipelineDesc.ColorAttachments.Object(0);
        colorAttachment.PixelFormat = MTLPixelFormat.RGBA32Float;
        pipelineDesc.ColorAttachments.SetObject(colorAttachment, 0);

        NSError psoError = default;
        _dePipelineState = _device.NewRenderPipelineState(pipelineDesc, ref psoError);
        if (_dePipelineState.NativePtr == IntPtr.Zero)
        {
            throw new Exception(string.Format("Error linking DE pipeline state: {0}", psoError.LocalizedDescription));
        }
        Console.WriteLine("[RendererGL] InitDEPass: End");
    }

    private void InitPostFxPingPongTextures()
    {
        Console.WriteLine("[RendererGL] InitPostFxPingPongTextures called");
        var descriptor = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.RGBA32Float, (nuint)HistogramWidth, (nuint)HistogramHeight, false);
        descriptor.Usage = MTLTextureUsage.ShaderRead | MTLTextureUsage.ShaderWrite | MTLTextureUsage.RenderTarget;
        descriptor.StorageMode = MTLStorageMode.Private;

        if (_postFxTextureA.NativePtr != IntPtr.Zero) _postFxTextureA.Dispose();
        _postFxTextureA = _device.NewTexture(descriptor);

        if (_postFxTextureB.NativePtr != IntPtr.Zero) _postFxTextureB.Dispose();
        _postFxTextureB = _device.NewTexture(descriptor);
    }

    private void InitPostFxPasses(IEnumerable<EffectPlugin> postFxs)
    {
        Console.WriteLine("[RendererGL] InitPostFxPasses: Start");
        _postFxPipelineStates.Clear();

        foreach (var postfx in postFxs)
        {
            _postFxPipelineStates.Add(InitPostFxPass(postfx));
        }
        Console.WriteLine("[RendererGL] InitPostFxPasses: End");
    }

    private MTLRenderPipelineState InitPostFxPass(EffectPlugin postfx)
    {
        Console.WriteLine($"[RendererGL] InitPostFxPass: Compiling postfx '{postfx.Name}'");
        var assembly = typeof(RendererGL).GetTypeInfo().Assembly;
        var resource = assembly.GetManifestResourceStream(_shadersPath + "postfx.frag.metal");
        if (resource == null)
        {
            throw new FileNotFoundException("Shader resource not found: postfx.frag.metal");
        }
        
        string shaderTemplate = new StreamReader(resource).ReadToEnd();
        
        // 1. Automatically rewrite GLSL array syntax to Metal float4 vector access
        string adaptedEffectSource = System.Text.RegularExpressions.Regex.Replace(
            postfx.SourceCode, 
            @"postfx_real_params\[(\d+)\]", 
            "params.postfx_real_params[$1].x"
        );

        // Define local pointers and type bridges so GLSL-style snippets find them seamlessly
        string postFxBridge = @"
        constant float3* postfx_vec3_params = params.postfx_vec3_params;
        #define width params.width
        #define height params.height
        #define vec2 float2
        #define vec3 float3
        #define vec4 float4
        #define texture(tex, coord) ((tex).sample(input_texSmplr, coord))
        
        float4 output_color = input_color;
        
        #define getPaletteColor(index) lookupPaletteColor(index, _palette_ubo, params.palette_colors_count)";
        string shaderSource = shaderTemplate.Replace("@includes", _includesSource);
        shaderSource = shaderSource.Replace("@PostFxSnippet", postFxBridge + "\n" + adaptedEffectSource);

        NSError fragError = default;
        var fragLibrary = _device.NewLibrary(shaderSource, new MTLCompileOptions(), ref fragError);
        
        if (fragError.NativePtr != IntPtr.Zero)
        {
            throw new Exception(string.Format("Error compiling postfx shader source:\n{0}", fragError.LocalizedDescription));
        }

        var fragFunc = fragLibrary.NewFunction("main0");
        if (fragFunc.NativePtr == IntPtr.Zero)
        {
            throw new Exception(string.Format("Error locating 'main0' function in postfx '{0}' shader.", postfx.Name));
        }

        var pipelineDesc = new MTLRenderPipelineDescriptor();
        pipelineDesc.VertexFunction = _vertexFunction; 
        pipelineDesc.FragmentFunction = fragFunc;

        var colorAttachment = pipelineDesc.ColorAttachments.Object(0);
        colorAttachment.PixelFormat = MTLPixelFormat.RGBA32Float;
        pipelineDesc.ColorAttachments.SetObject(colorAttachment, 0);

        NSError psoError = default;
        var pipelineState = _device.NewRenderPipelineState(pipelineDesc, ref psoError);
        
        if (pipelineState.NativePtr == IntPtr.Zero || psoError.NativePtr != IntPtr.Zero)
        {
            string errorDesc = psoError.NativePtr != IntPtr.Zero ? (psoError.LocalizedDescription.ToString() ?? "Unknown pipeline error") : "Unknown pipeline error";
            throw new Exception(string.Format("Error linking postfx '{0}' pipeline state: {1}", postfx.Name, errorDesc));
        }

        Console.WriteLine($"[RendererGL] InitPostFxPass: Finished compiling postfx '{postfx.Name}'");

        return pipelineState;
    }

    private void ApplyPostFxPasses(MTLCommandBuffer? externalCommandBuffer = null)
    {
        var enabledLayers = LoadedParams.PostEffects.Where(i => i.Enabled).ToList();
        if (enabledLayers.Count == 0)
            return;

        Console.WriteLine($"[RendererFL] ApplyPostFxPasses: Applying {enabledLayers.Count} effect(s)");
        var commandBuffer = externalCommandBuffer ?? _commandQueue.CommandBuffer();

        var blitEncoder = commandBuffer.BlitCommandEncoder();
        if ((_renderTexture.NativePtr != IntPtr.Zero) && (_postFxTextureA.NativePtr != IntPtr.Zero))
        {
            blitEncoder.CopyFromTexture(
                _renderTexture, 0, 0, new MTLOrigin { x = 0, y = 0, z = 0 }, new MTLSize { width = (ulong)HistogramWidth, height = (ulong)HistogramHeight, depth = 1 },
                _postFxTextureA, 0, 0, new MTLOrigin { x = 0, y = 0, z = 0 }
            );
        }
        blitEncoder.EndEncoding();

        for (int i = 0; i < enabledLayers.Count; i++)
        {
            var layer = enabledLayers[i];
            int programIndex = _registeredPostfxs?.IndexOf(layer.Effect) ?? -1;
            if (programIndex < 0 || programIndex >= _postFxPipelineStates.Count)
                throw new Exception($"Postfx pipeline state not found for effect '{layer.Effect.Name}'.");

            var pipelineState = _postFxPipelineStates[programIndex];
            var sourceTex = (i % 2 == 0) ? _postFxTextureA : _postFxTextureB;
            var destTex = (i % 2 == 0) ? _postFxTextureB : _postFxTextureA;

            if ((destTex.NativePtr == IntPtr.Zero) || (sourceTex.NativePtr == IntPtr.Zero)) continue;

            var renderPassDesc = new MTLRenderPassDescriptor();
            var colorAttachment = renderPassDesc.ColorAttachments.Object(0);
            colorAttachment.Texture = (MTLTexture)destTex;  
            colorAttachment.LoadAction = MTLLoadAction.Clear;
            colorAttachment.StoreAction = MTLStoreAction.Store;
            colorAttachment.ClearColor = new MTLClearColor { red = 0, green = 0, blue = 0, alpha = 1 };

            var renderEncoder = commandBuffer.RenderCommandEncoder(renderPassDesc);
            renderEncoder.SetRenderPipelineState(pipelineState);

            // 1. Bind source texture to index 0
            renderEncoder.SetFragmentTexture(sourceTex, 0);

            // 2. Pack parameters directly into a binary block matching the Metal struct layout
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(HistogramWidth);   // width (4 bytes)
            writer.Write(HistogramHeight);  // height (4 bytes)
            
            // CRITICAL: 8 bytes of padding to match Metal's 16-byte alignment boundary for arrays
            writer.Write(0L);               

            // realParams: float4[64]
            float[] realParams = layer.RealParams.Values.Select(v => (float)v).ToArray();
            for (int r = 0; r < 64; r++)
            {
                float val = (r < realParams.Length) ? realParams[r] : 0f;
                writer.Write(val); // x
                writer.Write(0f);  // y
                writer.Write(0f);  // z
                writer.Write(0f);  // w
            }

            // vec3Params: float3[64] with 16-byte alignment padding per element
            var vec3Values = layer.Vec3Params.Values.SelectMany(v => new float[] { (float)v.X, (float)v.Y, (float)v.Z }).ToArray();
            for (int vIdx = 0; vIdx < 64; vIdx++)
            {
                float vx = (vIdx * 3 + 0 < vec3Values.Length) ? vec3Values[vIdx * 3 + 0] : 0f;
                float vy = (vIdx * 3 + 1 < vec3Values.Length) ? vec3Values[vIdx * 3 + 1] : 0f;
                float vz = (vIdx * 3 + 2 < vec3Values.Length) ? vec3Values[vIdx * 3 + 2] : 0f;
                
                writer.Write(vx);
                writer.Write(vy);
                writer.Write(vz);
                writer.Write(0f); // padding float for Metal float3 array alignment
            }

            writer.Write(LoadedParams.Palette.Colors.Count); // palette_colors_count

            byte[] paramBytes = ms.ToArray();
            var paramHandle = GCHandle.Alloc(paramBytes, GCHandleType.Pinned);
            try
            {
                renderEncoder.SetFragmentBytes(paramHandle.AddrOfPinnedObject(), (nuint)paramBytes.Length, 0);
            }
            finally
            {
                paramHandle.Free();
            }

            renderEncoder.DrawPrimitives(MTLPrimitiveType.TriangleStrip, 0, 4);
            renderEncoder.EndEncoding();
        }

        // Copy final result back to main render texture
        var finalTex = (enabledLayers.Count % 2 == 0) ? _postFxTextureA : _postFxTextureB;
        if ((finalTex.NativePtr != IntPtr.Zero) && (_renderTexture.NativePtr != IntPtr.Zero))
        {
            var finalBlitEncoder = commandBuffer.BlitCommandEncoder();
            finalBlitEncoder.CopyFromTexture(
                finalTex, 0, 0, new MTLOrigin { x = 0, y = 0, z = 0 }, new MTLSize { width = (ulong)HistogramWidth, height = (ulong)HistogramHeight, depth = 1 },
                _renderTexture, 0, 0, new MTLOrigin { x = 0, y = 0, z = 0 }
            );
            finalBlitEncoder.EndEncoding();
        }

        if (externalCommandBuffer == null)
        {
            commandBuffer.Commit();
        }
    }

    private void InitComputeProgram(IEnumerable<TransformPlugin> transforms)
    {
        Console.WriteLine("[RendererGL] InitComputeProgram: Start");

        string transformsSource = "";
        int tfIndex = 0;
        foreach (var tf in transforms)
        {
            transformsSource += string.Format(@"
if (iter.tfId == {0})
{{
{1}
}}
", tfIndex, tf.SourceCode);
            tfIndex++;
        }

        var assembly = typeof(RendererGL).GetTypeInfo().Assembly;

        var resource = assembly.GetManifestResourceStream(_shadersPath + "ifs_kernel.comp.metal");
        if (resource == null)
        {
            throw new FileNotFoundException("Compute shader resource not found.");
        }

        string computeShaderSource = new StreamReader(resource).ReadToEnd();
        computeShaderSource = computeShaderSource.Replace("@includes", _includesSource);
        computeShaderSource = computeShaderSource.Replace("@transforms", transformsSource);

        MTLLibrary? computeLibrary = null;
        var compileOptions = new MTLCompileOptions();
        NSError compileError = default;

        computeLibrary = _device.NewLibrary(computeShaderSource, compileOptions, ref compileError);

        if (compileError.NativePtr != IntPtr.Zero)
        {
            throw new Exception(string.Format("Error compiling compute shader source:\n{0}", compileError.LocalizedDescription));
        }

        if (!computeLibrary.HasValue)
        {
            throw new Exception(string.Format("Error compiling compute shader: {0}", compileError.LocalizedDescription));
        }

        var kernelFunc = computeLibrary.Value.NewFunction("main0");   

        NSError computeError = default;
        _computePipelineState = _device.NewComputePipelineState(kernelFunc, ref computeError);

        if (_computePipelineState.NativePtr == IntPtr.Zero)
        {
            throw new Exception("Failed to create compute pipeline state.");
        }

        Console.WriteLine("[RendererGL] InitComputeProgram: Finished");
    }

    public unsafe void Test()
    {
        Console.WriteLine("[Test] Starting Metal Compute Circle Test...");

        if ((_device.NativePtr == IntPtr.Zero) || (_device.NativePtr == IntPtr.Zero))
        {
            Console.WriteLine("[Test] Error: Metal device is not initialized.");
            return;
        }

        int width = 1350;
        int height = 759;
        int totalPixels = width * height;

        // 11. Readback and analyze histogram buffer data
        Vector4* histPtr = (Vector4*)_histogramBuffer.Contents.ToPointer();
        float minVal = float.MaxValue;
        float maxVal = float.MinValue;
        int nonZeroPixels = 0;
        long totalHits = 0;

        for (int i = 0; i < totalPixels; i++)
        {
            float val = histPtr[i].W;
            if (val < minVal) minVal = val;
            if (val > maxVal) maxVal = val;

            if (val > 0.0f)
            {
                nonZeroPixels++;
                totalHits += (long)val;
            }
        }

        Console.WriteLine("----------------------------------------");
        Console.WriteLine("[Test] IFS Metal Compute Execution Complete!");
        Console.WriteLine($"[Test] Histogram Hit Range: Min={minVal}, Max={maxVal}");
        Console.WriteLine($"[Test] Non-Zero Pixels Hit: {nonZeroPixels:N0} / {totalPixels:N0}");
        Console.WriteLine($"[Test] Total Accumulated Hits: {totalHits:N0}");
        Console.WriteLine("----------------------------------------");

        // 12. Save buffer as PPM to Desktop with Adjustable Exposure
        if (maxVal <= 0.00001f) maxVal = 1.0f;

        // --- TONE MAPPING CONTROLS ---
        float exposure = 0.0001f; // Lower this (e.g. 0.02 to 0.1) to make it darker/more gradient-rich
        // -----------------------------

        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string filePath = Path.Combine(desktopPath, "ifs_fractal.png");

        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var writer = new StreamWriter(fs, System.Text.Encoding.ASCII))
        {
            writer.Write($"P6\n{width} {height}\n255\n");
            writer.Flush();

            byte[] pixelBuffer = new byte[totalPixels * 3];
            int pixelIndex = 0;
            
            float logMax = (float)Math.Log((maxVal * exposure) + 1.0f);
            if (logMax <= 0.0f) logMax = 1.0f;

            for (int i = 0; i < totalPixels; i++)
            {
                float hits = histPtr[i].W;
                float intensity = 0.0f;

                if (hits > 0.0f)
                {
                    // Apply exposure scaling before log compression
                    float logCurrent = (float)Math.Log((hits * exposure) + 1.0f);
                    intensity = logCurrent / logMax;
                    intensity = (float)Math.Pow(intensity, 1.0f / 2.2f); // Gamma correction
                }

                byte b = (byte)(Math.Clamp(intensity, 0.0f, 1.0f) * 255.0f);

                pixelBuffer[pixelIndex++] = b;
                pixelBuffer[pixelIndex++] = b;
                pixelBuffer[pixelIndex++] = b;
            }
            fs.Write(pixelBuffer, 0, pixelBuffer.Length);
        }

        Console.WriteLine($"[Test] Successfully saved fractal PPM to: {filePath}");
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public Vector4 position; 
        public Vector4 color;
    }

    private void InitBuffers()
    {
        Console.WriteLine("[RendererGL] InitBuffers called");
        nuint settingsSize = (nuint)Marshal.SizeOf<SettingsStruct>();
        nuint histogramSize = (nuint)(HistogramWidth * HistogramHeight * 4 * sizeof(float));
        nuint pointsSize = (nuint)(InvocationCount * Marshal.SizeOf<PointStruct>()); 

        _settingsBuffer = _device.NewBuffer(settingsSize, MTLResourceOptions.ResourceStorageModeShared);
        _histogramBuffer = _device.NewBuffer(histogramSize, MTLResourceOptions.ResourceStorageModeShared);
        _pointsBuffer = _device.NewBuffer(pointsSize, MTLResourceOptions.ResourceStorageModePrivate);
    }

    private static InvalidOperationException NewNotInitializedException() => new("Renderer is not initialized.");

    public async ValueTask DisposeAsync()
    {
        Console.WriteLine("[RendererGL] DisposeAsync called");
        DisplayFramebufferUpdated = null;
        if (IsInitialized)
        {
            if (IsRendering)
                await StopRenderLoop();

            // Clean up Pipelines
            if (_tonemapPipelineState.NativePtr != IntPtr.Zero)
                _tonemapPipelineState.Dispose();
            if (_dePipelineState.NativePtr != IntPtr.Zero)
                _dePipelineState.Dispose();

            // Clean up Buffers
            if (_settingsBuffer.Contents != IntPtr.Zero) 
                _settingsBuffer.Dispose();
            if (_histogramBuffer.Contents != IntPtr.Zero)
                _histogramBuffer.Dispose();
            if (_pointsBuffer.Contents != IntPtr.Zero)
                _pointsBuffer.Dispose();
            if (_iteratorsBuffer.Contents != IntPtr.Zero) 
                _iteratorsBuffer.Dispose();
            if (_aliasBuffer.Contents != IntPtr.Zero) 
                _aliasBuffer.Dispose();
            if (_paletteBuffer.Contents != IntPtr.Zero) 
                _paletteBuffer.Dispose();
            if (_realParametersBuffer.Contents != IntPtr.Zero) 
                _realParametersBuffer.Dispose();
            if (_vec3ParametersBuffer.Contents != IntPtr.Zero) 
                _vec3ParametersBuffer.Dispose();

            // Clean up Textures
            if (_renderTexture.NativePtr != IntPtr.Zero)
                _renderTexture.Dispose();
            if (_postFxTextureA.NativePtr != IntPtr.Zero)
                _postFxTextureA.Dispose();
            if (_postFxTextureB.NativePtr != IntPtr.Zero)
                _postFxTextureB.Dispose();
        }
        IsInitialized = false;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct GlobalsStruct
{
    public uint seed;
    public int width;
    public int height;
    public int dispatch_cnt;
    public int reset_points_state;
    public int invocation_iters;
}

[StructLayout(LayoutKind.Sequential)]
public struct CameraParamsStruct
{
    public Matrix4x4 view_proj_mat;
    public Vector4 position;
    public Vector4 forward;
    public Vector4 focus_point;
    public float aperture;
    public float focus_distance;
    public float depth_of_field;
    public int projection_type;
    public int padding0;
    public int padding1;
    public int padding2;
}

[StructLayout(LayoutKind.Sequential)]
public struct SettingsUboStruct
{
    public CameraParamsStruct camera;
    public float fog_effect;
    public int itnum;
    public int palettecnt;
    public int mark_area_in_focus;
    public int warmup;
    public float entropy;
    public int max_filter_radius;
    public int padding0;
    public int filter_method;
    public float filter_param0;
    public float filter_param1;
    public float filter_param2;
}

[StructLayout(LayoutKind.Sequential)]
public struct PaletteUboStruct
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 150)]
    public Vector4[] palette;
}

[StructLayout(LayoutKind.Sequential)]
public struct IteratorStruct
{
    public float color_speed;
    public float color_index;
    public float opacity;
    public float reset_prob;
    public int reset_alias;
    public int tfId;
    public int real_params_index;
    public int vec3_params_index;
    public int shading_mode;
    public float tf_mix;
    public float tf_add;
    public int padding2;
}

[StructLayout(LayoutKind.Sequential)]
public struct IteratorsUboStruct
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
    public IteratorStruct[] iterators;
}

[StructLayout(LayoutKind.Sequential)]
public struct AliasTablesUboStruct
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
    public Vector4[] alias_tables;
}

[StructLayout(LayoutKind.Sequential)]
public struct PStateStruct
{
    public Vector4 pos;
    public float color_index;
    public float dummy0;
    public int iterator_index;
    public int iteration_depth;
}

[StructLayout(LayoutKind.Sequential)]
public struct RealParamsUboStruct
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 200)]
    public float[] real_params;
}

[StructLayout(LayoutKind.Sequential)]
public struct Vec3ParamsUboStruct
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 200)]
    public Vector4[] vec3_params;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Float4Binding
{
    public float X, Y, Z, W;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Float3Binding
{
    public float X, Y, Z;
    public float Padding; // Required for Metal float3 alignment rules (16-byte boundary)
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PostFxShaderParams
{
    public int Width;
    public int Height;
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public Float4Binding[] RealParams;
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public Float3Binding[] Vec3Params;
    
    public int PaletteColorsCount;
}