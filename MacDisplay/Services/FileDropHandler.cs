#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Avalonia.Input;
using Avalonia.Platform.Storage;

using MacDisplay.Models;

namespace MacDisplay.Services;

internal class FileDropHandler
{
    private static readonly Dictionary<string, DroppedFile> _acceptedFileExtensions = new()
    {
        { ".gradient", DroppedFile.Palette },
        { ".ifsjson", DroppedFile.Params },
        { ".ifstf", DroppedFile.Transform },
        { ".png", DroppedFile.Image },
        { ".ugr", DroppedFile.Palette },
    };

    /// <returns>Copy effect when file extension is supported or None.</returns>
    public static DragDropEffects GetDragDropEffect(DragEventArgs e)
    {
        var ext = Path.GetExtension(GetSingleFilePath(e)) ?? "";
        if (_acceptedFileExtensions.ContainsKey(ext))
            return DragDropEffects.Copy;
        else
            return DragDropEffects.None;
    }

    /// <returns>True if the dropped file is supported.</returns>
    public static bool IsDropSupported(DragEventArgs e, out DroppedFile droppedFileType, out string filePath)
    {
        var path = GetSingleFilePath(e);
        filePath = path ?? string.Empty;
        var ext = Path.GetExtension(path);
        return _acceptedFileExtensions.TryGetValue(ext ?? "", out droppedFileType);
    }

    /// <returns>A single dragged file's path or null.</returns>
    private static string? GetSingleFilePath(DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles();
            var firstPath = files?.FirstOrDefault()?.TryGetLocalPath();
            return File.Exists(firstPath) ? firstPath : null;
        }
        return null;
    }
}