using FlowBlox.Core.Constants;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FlowBlox.AvaloniaDesktop.Persistence.RecentProjects
{
    public sealed class RecentProjectsManager
    {
        private const int MaxRecentProjects = 10;
        private static readonly Lazy<RecentProjectsManager> _lazyInstance = new(() => new RecentProjectsManager());
        private readonly object _sync = new();
        private readonly string _storageFilePath;

        public static RecentProjectsManager Instance => _lazyInstance.Value;

        private RecentProjectsManager()
        {
            _storageFilePath = GlobalPaths.RecentProjectsPath;
        }

        public IReadOnlyList<RecentProjectEntry> GetRecentProjects()
        {
            lock (_sync)
            {
                return LoadEntries()
                    .OrderByDescending(x => x.LastOpenedUtc)
                    .Take(MaxRecentProjects)
                    .ToList();
            }
        }

        public void RegisterOpenedProject(FlowBloxProject project, string projectFilePath, string projectSpaceGuid)
        {
            var normalizedFilePath = IOUtil.NormalizePath(projectFilePath);
            var normalizedGuid = NormalizeGuid(projectSpaceGuid);

            if (string.IsNullOrWhiteSpace(normalizedFilePath) && string.IsNullOrWhiteSpace(normalizedGuid))
                return;

            lock (_sync)
            {
                var entries = LoadEntries();
                entries.RemoveAll(x => IsSameProject(x, normalizedFilePath, normalizedGuid));

                entries.Insert(0, new RecentProjectEntry
                {
                    ProjectName = project?.ProjectName ?? string.Empty,
                    ProjectFilePath = normalizedFilePath,
                    ProjectSpaceGuid = normalizedGuid,
                    ApiEndpoint = project?.ProjectSpaceEndpointUri,
                    LastOpenedUtc = DateTime.UtcNow
                });

                SaveEntries(entries
                    .OrderByDescending(x => x.LastOpenedUtc)
                    .Take(MaxRecentProjects)
                    .ToList());
            }
        }

        public void RemoveEntry(RecentProjectEntry entry)
        {
            if (entry == null)
                return;

            lock (_sync)
            {
                var entries = LoadEntries();
                entries.RemoveAll(x => IsSameProject(x, entry.ProjectFilePath, entry.ProjectSpaceGuid));
                SaveEntries(entries);
            }
        }

        private List<RecentProjectEntry> LoadEntries()
        {
            try
            {
                if (!File.Exists(_storageFilePath))
                    return new List<RecentProjectEntry>();

                var json = File.ReadAllText(_storageFilePath);
                return JsonConvert.DeserializeObject<List<RecentProjectEntry>>(json) ?? new List<RecentProjectEntry>();
            }
            catch (Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);
                return new List<RecentProjectEntry>();
            }
        }

        private void SaveEntries(List<RecentProjectEntry> entries)
        {
            try
            {
                var directory = Path.GetDirectoryName(_storageFilePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonConvert.SerializeObject(entries ?? new List<RecentProjectEntry>(), Formatting.Indented);
                File.WriteAllText(_storageFilePath, json);
            }
            catch (Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);
            }
        }

        private static bool IsSameProject(RecentProjectEntry entry, string projectFilePath, string projectSpaceGuid)
        {
            if (entry == null)
                return false;

            var entryPath = IOUtil.NormalizePath(entry.ProjectFilePath);
            var comparePath = IOUtil.NormalizePath(projectFilePath);
            if (!string.IsNullOrWhiteSpace(entryPath) && !string.IsNullOrWhiteSpace(comparePath))
                return string.Equals(entryPath, comparePath, StringComparison.OrdinalIgnoreCase);

            var entryGuid = NormalizeGuid(entry.ProjectSpaceGuid);
            var compareGuid = NormalizeGuid(projectSpaceGuid);
            if (!string.IsNullOrWhiteSpace(entryGuid) && !string.IsNullOrWhiteSpace(compareGuid))
                return string.Equals(entryGuid, compareGuid, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        private static string NormalizeGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return string.Empty;

            return guid.Trim();
        }
    }
}
