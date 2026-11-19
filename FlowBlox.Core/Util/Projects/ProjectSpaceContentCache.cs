using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using Newtonsoft.Json;
using static FlowBlox.Core.ExternalServices.FlowBloxWebApi.FlowBloxWebApiService;

namespace FlowBlox.Core.Util.Projects;

/// <summary>
/// Shares downloaded Project Space definitions across processes; runtime instances are never cached.
/// </summary>
internal sealed class ProjectSpaceContentCache
{
    private readonly string _rootDirectory;

    internal ProjectSpaceContentCache(string rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsLocalAppDataDir).Value,
            "project-space-cache");
    }

    internal async Task<byte[]> LoadAsync(
        string projectSpaceGuid,
        int? version,
        string userToken,
        FlowBloxWebApiService api,
        bool cacheOnly = false)
    {
        var guid = Guid.Parse(projectSpaceGuid);
        var endpoint = FlowBloxApiUrlNomalizer.Normalize(api.BaseUrl);
        var versionName = version?.ToString(CultureInfo.InvariantCulture) ?? "latest";
        var directory = Path.Combine(_rootDirectory, Hash(Encoding.UTF8.GetBytes(endpoint)), guid.ToString("N"), versionName);
        var file = Path.Combine(directory, "project.json");
        var context = $"Project Space GUID={guid:N}, Version={versionName}";
        var logger = FlowBloxLogManager.Instance.GetLogger();

        if (cacheOnly)
        {
            var cached = await ReadAsync(file);
            if (cached == null)
                throw new InvalidOperationException($"No valid cached project is available ({context}). Preload the project before cache-only execution.");

            logger.Info($"Using cached project without a server request. {context}");
            return Convert.FromBase64String(cached.Content);
        }

        Directory.CreateDirectory(directory);
        using var cacheLock = await AcquireLockAsync(Path.Combine(directory, "download.lock"));
        var metadataResponse = await api.GetProjectAsync(new FbProjectRequest { Guid = guid.ToString("N") }, userToken);
        RequireSuccess(metadataResponse, userToken, context);
        var metadata = metadataResponse.ResultObject;
        if (metadata == null)
        {
            RequireLogin(metadataResponse, userToken);
            throw new InvalidOperationException($"Project not found in Project Space ({context}).");
        }

        var fingerprint = GetFingerprint(metadata, version);
        var existing = await ReadAsync(file);
        if (fingerprint != null && existing?.Fingerprint == fingerprint)
        {
            logger.Info($"Project Space content is unchanged; using cached project. {context}");
            return Convert.FromBase64String(existing.Content);
        }

        logger.Info($"Downloading missing, changed or unverifiable Project Space content. {context}");
        var response = version.HasValue
            ? await api.GetProjectVersionContentAsync(userToken, guid, version.Value)
            : await api.GetProjectContentAsync(userToken, guid);
        RequireSuccess(response, userToken, context);
        if (string.IsNullOrWhiteSpace(response.ResultObject))
            throw new InvalidOperationException($"Project Space returned empty project content ({context}).");

        var bytes = Convert.FromBase64String(response.ResultObject);
        var entry = new ProjectSpaceCacheEntry
        {
            Fingerprint = fingerprint,
            ContentHash = Hash(bytes),
            Content = response.ResultObject
        };
        await WriteAsync(file, entry);
        logger.Info($"Project Space content downloaded and cached. {context}");
        return bytes;
    }

    private static string GetFingerprint(FbProjectResult metadata, int? version)
    {
        var created = metadata.CreatedAt;
        var changed = metadata.UpdatedAt;
        if (version.HasValue)
        {
            // Stable content is immutable; editing its comment does not invalidate the cached content.
            created = metadata.Versions?.FirstOrDefault(item => item.VersionNumber == version.Value)?.CreatedAt;
            changed = null;
            if (!created.HasValue)
                return null;
        }
        else if (!created.HasValue)
        {
            // Missing timestamps cannot prove that the current content is unchanged.
            return null;
        }

        return Hash(Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"{metadata.Guid}|{version}|{created:O}|{changed:O}")));
    }

    private static async Task<ProjectSpaceCacheEntry> ReadAsync(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var entry = JsonConvert.DeserializeObject<ProjectSpaceCacheEntry>(await reader.ReadToEndAsync());
            if (string.IsNullOrWhiteSpace(entry?.Content) || string.IsNullOrWhiteSpace(entry.ContentHash))
                return null;

            return Hash(Convert.FromBase64String(entry.Content)) == entry.ContentHash ? entry : null;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            FlowBloxLogManager.Instance.GetLogger().Warn("Ignoring invalid Project Space cache entry.", exception);
            return null;
        }
    }

    private static async Task WriteAsync(string file, ProjectSpaceCacheEntry entry)
    {
        var temporaryFile = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryFile, JsonConvert.SerializeObject(entry));
            File.Move(temporaryFile, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
                File.Delete(temporaryFile);
        }
    }

    private static async Task<FileStream> AcquireLockAsync(string file)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (started.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(50);
            }
        }
    }

    private static void RequireSuccess(ApiResponse response, string token, string context)
    {
        if (response?.Success == true)
            return;

        RequireLogin(response, token);
        throw new InvalidOperationException($"Failed to retrieve Project Space project ({context}). {response?.ErrorMessage}");
    }

    private static void RequireLogin(ApiResponse response, string token)
    {
        if (response != null && string.IsNullOrWhiteSpace(token) &&
            (response.Success || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound))
            throw new ProjectSpaceLoginRequiredException();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
