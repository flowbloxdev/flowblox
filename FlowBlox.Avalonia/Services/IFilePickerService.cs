using System.Threading.Tasks;

namespace FlowBlox.AvaloniaDesktop.Services;

public interface IFilePickerService
{
    Task<string> OpenProjectAsync(string directory);
    Task<string> SaveProjectAsync(string directory, string suggestedName);
    Task<string> SaveImageAsync(string directory);
}
