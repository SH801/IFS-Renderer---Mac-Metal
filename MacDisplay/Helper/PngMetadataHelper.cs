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
    /// Embeds XMP metadata and serialized parameters into a PNG image stream using cross-platform chunks.
    /// </summary>
    public static void EmbedMetadataInPngStream(Stream imageStream, Stream outputStream, IFS ifs, bool includeSerializedParams)
    {
        var authorNames = ifs.Authors.Select(author => author.Name).ToHashSet();
        string[] keywords = [
            "IFSRenderer",
            "IFS",
            "fractal",
            ifs.Title,
            .. authorNames,
            .. ifs.Iterators.Select(i => i.Transform.Name).Distinct(),
            ifs.Palette.Name];

        var serializedParams = includeSerializedParams ? IfsNodesSerializer.SerializeJsonString(ifs) : string.Empty;

        var xmpMetadata = XMPMetadataSerializer.Serialize(
            title: ifs.Title,
            authors: authorNames,
            description: serializedParams,
            subject: keywords);

        using var image = Image.Load(imageStream);
        
        var pngMetadata = image.Metadata.GetPngMetadata();
        pngMetadata.TextData.Add(new PngTextData("Comment", serializedParams, string.Empty, string.Empty));
        pngMetadata.TextData.Add(new PngTextData("XML:com.adobe.xmp", xmpMetadata, string.Empty, string.Empty));

        image.Save(outputStream, new PngEncoder());
    }

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

    /// <summary>
    /// Alias supporting existing file-loading calls.
    /// </summary>
    public static bool TryExtractParamsFromImageFile(string filePath, IEnumerable<TransformPlugin> transforms, IEnumerable<EffectPlugin> postfxs, out IFS embeddedParams)
        => TryExtractParamsFromImage(filePath, transforms, postfxs, out embeddedParams);
}