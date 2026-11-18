using System.Text.Json;
using FlowBlox.Core.Constants;

namespace FlowBlox.Core.Authentication.OAuth;

public sealed class OAuthTokenStore
{
    private readonly string _directory;

    public OAuthTokenStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Path.GetDirectoryName(GlobalPaths.GlobalConfigurationXml)!, "oauth");
    }

    public OAuthTokenSet? Read(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<OAuthTokenSet>(
            FlowBloxSecureStorageManager.GetProtectedData(key, File.ReadAllText(path)));
    }

    public void Write(string key, OAuthTokenSet tokens)
    {
        Directory.CreateDirectory(_directory);
        var path = GetPath(key);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, FlowBloxSecureStorageManager.SetProtectedData(key, JsonSerializer.Serialize(tokens)));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public void Delete(string key) => File.Delete(GetPath(key));

    public async Task<FileStream> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(GetPath(key) + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string GetPath(string key)
    {
        if (key.Length != 64 || !key.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid OAuth credential key.", nameof(key));
        return Path.Combine(_directory, key + ".credential");
    }
}
