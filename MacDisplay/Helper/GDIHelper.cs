#nullable enable
using System;
using System.IO;
using System.Runtime;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using MacDisplay.Helper;

namespace MacDisplay.Helper;

public static class GDIHelper
{
    public static async Task SaveBitmapAsync(WriteableBitmap bitmap, string titleHint)
    {
        var (success, path) = await DialogHelper.ShowExportImageDialogAsync(titleHint);
        if (success)
        {
            using var stream = File.Create(path);
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
        }
    }
}