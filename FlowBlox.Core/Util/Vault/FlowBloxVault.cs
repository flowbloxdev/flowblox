using FlowBlox.Core.Constants;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace FlowBlox.Core.Util.Vault;

/// <summary>
/// Local, user-protected environment values shared by projects. Only metadata is public to AI tools.
/// </summary>
public static class FlowBloxVault
{
    private static readonly object Sync = new();
    private static string _cachedToken;
    private static VaultEntry[] _cachedEntries = [];
    private static readonly Regex ReferencePattern = new(@"\$Vault::([A-Za-z0-9öäüÖÄÜ_-]+)", RegexOptions.Compiled);

    public static bool ContainsReference(string text) => text != null && ReferencePattern.IsMatch(text);

    public static IReadOnlyList<VaultEntry> Load() => GetSnapshot().Select(entry => new VaultEntry
    {
        Name = entry.Name,
        Description = entry.Description,
        Value = entry.Value,
        IsPassword = entry.IsPassword
    }).ToArray();

    public static IReadOnlyList<VaultReference> GetReferences() => GetSnapshot()
        .Select(entry => new VaultReference(
            entry.Name,
            entry.Description,
            entry.IsPassword,
            entry.Reference))
        .ToArray();

    private static VaultEntry[] GetSnapshot()
    {
        lock (Sync)
        {
            var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.VaultValues);
            if (option?.StorageToken == _cachedToken)
                return _cachedEntries;

            var json = option?.PersistentValue;
            var entries = string.IsNullOrWhiteSpace(json)
                ? []
                : JsonConvert.DeserializeObject<VaultEntry[]>(json) ?? [];
            ValidateEntries(entries);
            _cachedEntries = entries;
            _cachedToken = option?.StorageToken;
            return entries;
        }
    }

    private static void ValidateEntries(IEnumerable<VaultEntry> entries)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry == null || !Regex.IsMatch(entry.Name ?? string.Empty, @"^[A-Za-z0-9öäüÖÄÜ_-]+$")
                || !names.Add(entry.Name))
                throw new ArgumentException("Vault names must be unique and contain only letters, numbers, underscores or hyphens.");
        }
    }

    public static void Save(IEnumerable<VaultEntry> entries)
    {
        var snapshot = entries.ToList();
        ValidateEntries(snapshot);

        lock (Sync)
        {
            var options = FlowBloxOptions.GetOptionInstance();
            var option = options.GetOption(OptionsConstants.VaultValues);
            option.PersistentValue = JsonConvert.SerializeObject(snapshot);
            options.Save(throwOnError: true);
        }
    }

    public static string Resolve(string text)
    {
        if (!ContainsReference(text))
            return text;

        var values = GetSnapshot().ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        return ReferencePattern.Replace(text, match => values.TryGetValue(match.Groups[1].Value, out var entry)
            ? entry.Value ?? string.Empty
            : match.Value);
    }
}

