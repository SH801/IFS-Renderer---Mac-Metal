#nullable enable
using System;
using System.Runtime.InteropServices;

namespace MacDisplay.Helper;

// Force hardware acceleration for Nvidia Optimus laptops (Windows-specific)
public static partial class NvOptimusHelper
{
    [LibraryImport("nvapi64.dll", EntryPoint = "fake")]
    private static partial int LoadNvApi64();

    [LibraryImport("nvapi.dll", EntryPoint = "fake")]
    private static partial int LoadNvApi32();

    // To be called before first window creation
    public static void InitializeDedicatedGraphics()
    {
        // NVIDIA Optimus is Windows-only; macOS manages graphics switching natively.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            if (Environment.Is64BitProcess)
                _ = LoadNvApi64();
            else
                _ = LoadNvApi32();
        }
        catch (DllNotFoundException) { } // Not NVIDIA or non-Windows, ignore
        catch (EntryPointNotFoundException) { } // Will always fail since 'fake' entry point doesn't exist. This is expected.
    }
}