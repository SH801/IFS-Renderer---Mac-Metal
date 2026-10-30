#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IFSEngine.Model;
using MacDisplay.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;

namespace MacDisplay.Helper;

internal static class PngMetadataHelper
{
    /// <summary>
    /// Attempts to extract serialized parameters from a PNG image file's text chunks.
    /// </summary>
    public static bool TryExtractParamsFromImage(string filePath, IEnumerable<TransformPlugin> transforms, IEnumerable<EffectPlugin> postfxs, out IFS embeddedParams)
    {
        try
        {
            using var image = Image.Load(filePath);
            var pngMetadata = image.Metadata.GetPngMetadata();
            
            var commentChunk = pngMetadata.TextData.FirstOrDefault(t => t.Keyword == "Comment");
            if (string.IsNullOrEmpty(commentChunk.Value))
            {
                embeddedParams = null!;
                return false;
            }

            embeddedParams = IfsNodesSerializer.DeserializeJsonString(commentChunk.Value, transforms, postfxs, true);
            return true;
        }
        catch
        {
            embeddedParams = null!;
            return false;
        }
    }

}