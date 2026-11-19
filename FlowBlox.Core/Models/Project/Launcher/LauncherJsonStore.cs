using Newtonsoft.Json;
using FlowBlox.Core.Util;

namespace FlowBlox.Core.Models.Project.Launcher;

/// <summary>
/// Stores launcher lists atomically; a damaged file is never silently replaced with an empty list.
/// </summary>
public sealed class LauncherJsonStore<T>
{
    private readonly string _path;
    private readonly object _sync = new();

    public LauncherJsonStore(string path)
    {
        _path = path;
    }

    public List<T> Read()
    {
        lock (_sync)
        {
            using var storeLock = InterprocessFileLock.Acquire(_path);
            return ReadCore();
        }
    }

    public void Update(Action<List<T>> update)
    {
        lock (_sync)
        {
            using var storeLock = InterprocessFileLock.Acquire(_path);
            var entries = ReadCore();
            update(entries);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(entries, Formatting.Indented));
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }

    private List<T> ReadCore()
    {
        if (!File.Exists(_path))
            return new List<T>();

        return JsonConvert.DeserializeObject<List<T>>(
            File.ReadAllText(_path),
            new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })
            ?? throw new InvalidDataException($"Invalid launcher list: {_path}");
    }
}
