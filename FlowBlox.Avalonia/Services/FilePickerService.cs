using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace FlowBlox.AvaloniaDesktop.Services;

public sealed class FilePickerService(IOwnerService owners) : IFilePickerService
{
    private static readonly FilePickerFileType Projects = new("FlowBlox Project") { Patterns = new[] { "*.fbprj" } };
    private static readonly FilePickerFileType Images = new("PNG") { Patterns = new[] { "*.png" }, MimeTypes = new[] { "image/png" } };

    public async Task<string> OpenProjectAsync(string directory)
    {
        var storage = owners.GetCurrentOwner().StorageProvider;
        using var folder = await storage.TryGetFolderFromPathAsync(directory);
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false, FileTypeFilter = new[] { Projects }, SuggestedStartLocation = folder
        });
        using var file = files.SingleOrDefault();
        return LocalPath(file);
    }

    public Task<string> SaveProjectAsync(string directory, string suggestedName)
        => SaveAsync(directory, suggestedName, "fbprj", Projects);

    public Task<string> SaveImageAsync(string directory) => SaveAsync(directory, null, "png", Images);

    private async Task<string> SaveAsync(string directory, string name, string extension, FilePickerFileType type)
    {
        var storage = owners.GetCurrentOwner().StorageProvider;
        using var folder = await storage.TryGetFolderFromPathAsync(directory);
        using var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedStartLocation = folder, SuggestedFileName = name, DefaultExtension = extension,
            FileTypeChoices = new[] { type }, ShowOverwritePrompt = true
        });
        return LocalPath(file);
    }

    private static string LocalPath(IStorageFile file) => file == null ? null :
        file.TryGetLocalPath() ?? throw new NotSupportedException("FlowBlox projects currently require a local file path.");
}
