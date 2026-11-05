using FlowBlox.Core.Constants;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;

namespace FlowBlox.AIAssistant.History
{
    public sealed class AiAssistantHistoryStore
    {
        private const string HistorySearchPattern = "history_*.json*";
        private const string HistoryZipExtension = ".zip";
        private const string HistoryJsonEntryName = "history.json";
        private const int HistoryPreviewMaxLength = 255;
        private readonly object _sync = new();
        private bool _isInitialized;

        public static AiAssistantHistoryStore Instance { get; } = new();

        public ObservableCollection<AiAssistantHistoryListItem> Histories { get; } = new();

        public string GetHistoryDirectory()
        {
            var options = FlowBloxOptions.GetOptionInstance();
            var directory = options.GetOption(OptionsConstants.AIAssistantHistoryDirectory)?.Value;
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FlowBlox",
                    "ai_assistant_histories");
            }

            directory = Environment.ExpandEnvironmentVariables(directory);
            Directory.CreateDirectory(directory);
            return directory;
        }

        public void Initialize(Guid? projectGuid = null)
        {
            lock (_sync)
            {
                if (_isInitialized)
                    return;

                _isInitialized = true;
            }

            Refresh(projectGuid);
        }

        public void Refresh(Guid? projectGuid = null)
        {
            var items = LoadList(projectGuid);
            Histories.Clear();
            foreach (var item in items)
                Histories.Add(item);
        }

        public IReadOnlyList<AiAssistantHistoryListItem> LoadList(Guid? projectGuid = null)
        {
            var directory = GetHistoryDirectory();
            return Directory.EnumerateFiles(directory, HistorySearchPattern)
                .Where(IsSupportedHistoryFile)
                .Select(TryLoadListItem)
                .Where(x => x != null)
                .Select(x => x!)
                .Where(x => projectGuid == null || x.ProjectGuid == projectGuid.Value)
                .OrderByDescending(x => x.UpdatedAt)
                .ToList();
        }

        public AiAssistantHistoryDocument? Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            var document = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(ReadHistoryJson(filePath));
            if (document != null && document.HistoryGuid == Guid.Empty)
                document.HistoryGuid = Guid.NewGuid();

            return document;
        }

        public string CreateOrUpdateHistory(AiAssistantHistoryDocument document, string? filePath)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (document.HistoryGuid == Guid.Empty)
                document.HistoryGuid = Guid.NewGuid();

            if (document.CreatedAt == default)
                document.CreatedAt = DateTimeOffset.Now;

            document.UpdatedAt = DateTimeOffset.Now;

            if (string.IsNullOrWhiteSpace(filePath))
                filePath = Histories.FirstOrDefault(x => x.HistoryGuid == document.HistoryGuid)?.FilePath;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                filePath = Path.Combine(
                    GetHistoryDirectory(),
                    $"history_{document.CreatedAt:yyyyMMdd_HHmmss_fff}.json{HistoryZipExtension}");
            }
            else if (!IsZipHistoryFile(filePath))
            {
                var zipFilePath = filePath + HistoryZipExtension;
                if (!File.Exists(zipFilePath))
                    filePath = zipFilePath;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            WriteHistoryJson(filePath, JsonConvert.SerializeObject(document, Formatting.Indented));
            UpsertListItem(ToListItem(document, filePath));
            return filePath;
        }

        public void Delete(AiAssistantHistoryListItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            if (string.IsNullOrWhiteSpace(item.FilePath))
                throw new ArgumentException("AI assistant history file path is missing.", nameof(item));

            lock (_sync)
            {
                if (File.Exists(item.FilePath))
                    File.Delete(item.FilePath);

                var existing = Histories.FirstOrDefault(x =>
                    x.HistoryGuid == item.HistoryGuid ||
                    string.Equals(x.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                    Histories.Remove(existing);
            }
        }

        private void UpsertListItem(AiAssistantHistoryListItem item)
        {
            var existing = Histories.FirstOrDefault(x => x.HistoryGuid == item.HistoryGuid);
            if (existing != null)
            {
                var oldIndex = Histories.IndexOf(existing);
                if (oldIndex >= 0)
                    Histories.RemoveAt(oldIndex);
            }

            InsertSorted(item);
        }

        private void InsertSorted(AiAssistantHistoryListItem item)
        {
            var insertIndex = 0;
            while (insertIndex < Histories.Count && Histories[insertIndex].UpdatedAt > item.UpdatedAt)
                insertIndex++;

            Histories.Insert(insertIndex, item);
        }

        private static AiAssistantHistoryListItem? TryLoadListItem(string filePath)
        {
            try
            {
                var document = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(ReadHistoryJson(filePath));
                if (document == null)
                    return null;

                if (document.HistoryGuid == Guid.Empty)
                    document.HistoryGuid = Guid.NewGuid();

                return ToListItem(document, filePath);
            }
            catch
            {
                return null;
            }
        }

        private static AiAssistantHistoryListItem ToListItem(AiAssistantHistoryDocument document, string filePath)
        {
            return new AiAssistantHistoryListItem
            {
                HistoryGuid = document.HistoryGuid,
                FilePath = filePath,
                ProjectGuid = document.ProjectGuid,
                CreatedAt = document.CreatedAt,
                UpdatedAt = document.UpdatedAt,
                Preview = BuildPreviewText(document)
            };
        }

        private static string BuildPreviewText(AiAssistantHistoryDocument document)
        {
            var text = document?.Transcripts
                ?.LastOrDefault(x => x.Kind == AssistantTranscriptKind.User || x.Kind == AssistantTranscriptKind.Assistant)
                ?.Text ?? string.Empty;

            return TextHelper.ShortenString(text, HistoryPreviewMaxLength, removeLineBreaks: true);
        }

        private static bool IsSupportedHistoryFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            return filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                   filePath.EndsWith(".json" + HistoryZipExtension, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsZipHistoryFile(string filePath)
        {
            return filePath?.EndsWith(HistoryZipExtension, StringComparison.OrdinalIgnoreCase) == true;
        }

        private static string ReadHistoryJson(string filePath)
        {
            if (!IsZipHistoryFile(filePath))
                return File.ReadAllText(filePath);

            using var archive = ZipFile.OpenRead(filePath);
            var entry = archive.GetEntry(HistoryJsonEntryName)
                ?? archive.Entries.FirstOrDefault(x => x.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                throw new InvalidOperationException("AI assistant history archive does not contain a JSON history entry.");

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static void WriteHistoryJson(string filePath, string json)
        {
            if (!IsZipHistoryFile(filePath))
            {
                File.WriteAllText(filePath, json);
                return;
            }

            var tempFilePath = filePath + ".tmp";
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);

            using (var archive = ZipFile.Open(tempFilePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(HistoryJsonEntryName, CompressionLevel.Optimal);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(json);
            }

            if (File.Exists(filePath))
                File.Delete(filePath);

            File.Move(tempFilePath, filePath);
        }
    }
}