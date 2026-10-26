#nullable enable
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace MacDisplay.Helper;

public static class DialogHelper
{
    private static readonly FilePickerFileType ParamsFilter = new("IFSRenderer params")
    {
        Patterns = ["*.ifsjson", "*.json"]
    };

    private static readonly FilePickerFileType ImageFilter = new("PNG Images")
    {
        Patterns = ["*.png"]
    };

    private static readonly FilePickerFileType ExrFilter = new("EXR Images")
    {
        Patterns = ["*.exr"]
    };

    private static readonly FilePickerFileType PaletteFilter = new("Flame Palettes")
    {
        Patterns = ["*.gradient", "*.ugr"]
    };

    private static readonly FilePickerFileType AudioFilter = new("Audio files")
    {
        Patterns = ["*.*"]
    };

    private static readonly FilePickerFileType FfmpegExeFilter = new("FFmpeg executable")
    {
        Patterns = ["ffmpeg", "ffmpeg.exe"]
    };

    private static readonly FilePickerFileType ChannelsCsvFilter = new("Animation channels")
    {
        Patterns = ["*.csv"]
    };

    private static Window? GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    public static async Task ShowMessageBoxAsync(string title, string message)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var dialog = new Window
        {
            Title = title,
            Width = 350,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var button = new Button 
        { 
            Content = "OK", 
            HorizontalAlignment = HorizontalAlignment.Center, 
            Width = 80 
        };
        button.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                button
            }
        };

        await dialog.ShowDialog(mainWindow);
    }

    public static async Task<(bool Success, string FilePath)> ShowOpenParamsDialogAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return (false, string.Empty);

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open params",
            AllowMultiple = false,
            FileTypeFilter = [ParamsFilter]
        });

        if (files.Count > 0)
        {
            return (true, files[0].Path.LocalPath);
        }
        return (false, string.Empty);
    }

    public static async Task<(bool Success, string FilePath)> ShowSaveParamsDialogAsync(string filenameHint)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return (false, string.Empty);

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save params",
            DefaultExtension = "ifsjson",
            SuggestedFileName = filenameHint,
            FileTypeChoices = [ParamsFilter]
        });

        if (file != null)
        {
            return (true, file.Path.LocalPath);
        }
        return (false, string.Empty);
    }

    public static async Task<(bool Success, string FilePath)> ShowExportImageDialogAsync(string filenameHint)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return (false, string.Empty);

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export image",
            DefaultExtension = "png",
            SuggestedFileName = filenameHint,
            FileTypeChoices = [ImageFilter]
        });

        if (file != null)
        {
            return (true, file.Path.LocalPath);
        }
        return (false, string.Empty);
    }

    public static async Task<(bool Success, string FilePath)> ShowExportExrDialogAsync(string filenameHint)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return (false, string.Empty);

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export hdr image",
            DefaultExtension = "exr",
            SuggestedFileName = filenameHint,
            FileTypeChoices = [ExrFilter]
        });

        if (file != null)
        {
            return (true, file.Path.LocalPath);
        }
        return (false, string.Empty);
    }

    public static async Task<(bool Success, string FilePath)> ShowOpenPaletteDialogAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return (false, string.Empty);

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open palette",
            AllowMultiple = false,
            FileTypeFilter = [PaletteFilter]
        });

        if (files.Count > 0)
        {
            return (true, files[0].Path.LocalPath);
        }
        return (false, string.Empty);
    }

    public static async Task<(bool Success, string FilePath)> ShowOpenAudioDialogAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return (false, string.Empty);

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open audio",
            AllowMultiple = false,
            FileTypeFilter = [AudioFilter]
        });

        if (files.Count > 0)
        {
            return (true, files[0].Path.LocalPath);
        }
        return (false, string.Empty);
    }

    public static async Task<(bool Success, string FolderPath)> ShowAnimationFolderBrowserDialogAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return (false, string.Empty);

        var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the folder where animation frames will be saved.",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            return (true, folders[0].Path.LocalPath);
        }
        return (false, string.Empty);
    }

    public static async Task<(bool Success, string ExePath)> ShowFfmpegPathSelectorDialogAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return (false, string.Empty);

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select FFmpeg executable",
            AllowMultiple = false,
            FileTypeFilter = [FfmpegExeFilter]
        });

        if (files.Count > 0)
        {
            return (true, files[0].Path.LocalPath);
        }
        return (false, string.Empty);
    }

    public static async Task<(bool Success, string CsvPath)> ShowImportChannelsDialogAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return (false, string.Empty);

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the CSV file to import animation channels from.",
            AllowMultiple = false,
            FileTypeFilter = [ChannelsCsvFilter]
        });

        if (files.Count > 0)
        {
            return (true, files[0].Path.LocalPath);
        }
        return (false, string.Empty);
    }
}