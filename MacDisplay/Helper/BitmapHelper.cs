#nullable enable
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using IFSEngine.Rendering;

namespace MacDisplay.Helper;

public static class BitmapHelper
{
    /// <summary>
    /// Converts the raw pixel data into a BitmapSource using Avalonia-specific bitmap rendering.
    /// The Alpha channel is optionally handled and the image pixels are processed according to export requirements.
    /// </summary>
    public static async Task<Bitmap> GetExportBitmapSource(this RendererGL renderer, bool transparentBackground)
    {
        int width = renderer.HistogramWidth;
        int height = renderer.HistogramHeight;

        var writeableBitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using (var framebuffer = writeableBitmap.Lock())
        {
            // 1. Copy the raw pixel data directly into the bitmap buffer
            await renderer.CopyPixelDataToBitmap(framebuffer.Address);

            // 2. Apply background color if transparency is disabled
            if (!transparentBackground)
            {
                var bgColor = renderer.LoadedParams.BackgroundColor;
                byte bgB = bgColor.B;
                byte bgG = bgColor.G;
                byte bgR = bgColor.R;

                unsafe
                {
                    byte* ptr = (byte*)framebuffer.Address;
                    int stride = framebuffer.RowBytes;

                    for (int y = 0; y < height; y++)
                    {
                        byte* row = ptr + (y * stride);
                        for (int x = 0; x < width; x++)
                        {
                            int idx = x * 4;
                            byte b = row[idx + 0];
                            byte g = row[idx + 1];
                            byte r = row[idx + 2];
                            byte a = row[idx + 3];

                            if (a == 0)
                            {
                                // Fully transparent: replace with background color
                                row[idx + 0] = bgB;
                                row[idx + 1] = bgG;
                                row[idx + 2] = bgR;
                                row[idx + 3] = 255; // Opaque alpha
                            }
                            else if (a < 255)
                            {
                                // Semi-transparent: alpha blend over background
                                float alpha = a / 255.0f;
                                float invAlpha = 1.0f - alpha;

                                row[idx + 0] = (byte)(b + (bgB * invAlpha));
                                row[idx + 1] = (byte)(g + (bgG * invAlpha));
                                row[idx + 2] = (byte)(r + (bgR * invAlpha));
                                row[idx + 3] = 255; // Fully opaque result
                            }
                        }
                    }
                }
            }
        }

        // Handle vertical flipping if required by your layout coordinate system
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Add row flipping here if exports appear upside-down
        });

        return writeableBitmap;
    }
}