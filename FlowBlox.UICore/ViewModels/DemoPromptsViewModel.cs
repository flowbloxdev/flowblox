using FlowBlox.Core.Constants;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DistributedData;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Views;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace FlowBlox.UICore.ViewModels
{
    public sealed class DemoPromptsViewModel : INotifyPropertyChanged
    {
        private readonly DemoPromptsWindow _window;
        private DemoPromptItem? _selectedPrompt;

        public ObservableCollection<DemoPromptItem> Prompts { get; } = new();
        public RelayCommand BackCommand { get; }
        public RelayCommand UseCommand { get; }

        public DemoPromptItem? SelectedPrompt
        {
            get => _selectedPrompt;
            set
            {
                if (ReferenceEquals(_selectedPrompt, value))
                    return;

                _selectedPrompt = value;
                OnPropertyChanged();
                UseCommand.Invalidate();
            }
        }

        public DemoPromptsViewModel(DemoPromptsWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            BackCommand = new RelayCommand(() => _window.DialogResult = false);
            UseCommand = new RelayCommand(UseSelectedPrompt, () => SelectedPrompt != null);
            LoadPrompts();
        }

        private void LoadPrompts()
        {
            var dataType = DistributedDataCatalog.GetDataTypes()
                .FirstOrDefault(x => string.Equals(
                    x.Name,
                    DistributedDataCatalog.DemoPromptType,
                    StringComparison.OrdinalIgnoreCase));
            if (dataType == null)
                return;

            var languageCode = GetLanguageCode();
            foreach (var fileName in dataType.FileNames)
            {
                var defaultPromptFile = DistributedDataCatalog.ResolvePath(dataType.Name, fileName);
                if (string.IsNullOrWhiteSpace(defaultPromptFile))
                    continue;

                try
                {
                    var metadataFile = Path.ChangeExtension(defaultPromptFile, ".metadata");
                    var metadata = File.Exists(metadataFile)
                        ? JObject.Parse(File.ReadAllText(metadataFile))
                        : new JObject();
                    var localizedPromptFile = ResolveLocalizedPromptFile(
                        dataType.Name,
                        fileName,
                        languageCode) ?? defaultPromptFile;
                    var title = ResolveTitle(metadata, languageCode, fileName);
                    var createdAt = ResolveCreatedAt(metadata, defaultPromptFile);

                    Prompts.Add(new DemoPromptItem(
                        title,
                        File.ReadAllText(localizedPromptFile).Trim(),
                        createdAt));
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        $"Failed to read demo prompt '{defaultPromptFile}'.",
                        ex);
                }
            }

            var orderedPrompts = Prompts
                .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            Prompts.Clear();
            foreach (var prompt in orderedPrompts)
                Prompts.Add(prompt);
        }

        private static string GetLanguageCode()
        {
            var configuredCulture = FlowBloxOptions
                .GetOptionInstance()
                .GetOption(OptionsConstants.UICulture)?
                .Value;

            try
            {
                return string.IsNullOrWhiteSpace(configuredCulture)
                    ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                    : CultureInfo.GetCultureInfo(configuredCulture).TwoLetterISOLanguageName;
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            }
        }

        private static string? ResolveLocalizedPromptFile(
            string dataType,
            string fileName,
            string languageCode)
        {
            if (string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase))
                return null;

            var localizedFileName = $"{Path.GetFileNameWithoutExtension(fileName)}.{languageCode}.txt";
            return DistributedDataCatalog.ResolvePath(dataType, localizedFileName);
        }

        private static string ResolveTitle(JObject metadata, string languageCode, string fileName)
        {
            var title = metadata["title"] as JObject;
            return title?.Value<string>(languageCode)?.Trim() ??
                   title?.Value<string>("default")?.Trim() ??
                   Path.GetFileNameWithoutExtension(fileName);
        }

        private static DateTime ResolveCreatedAt(JObject metadata, string defaultPromptFile)
        {
            var createdAtToken = metadata["createdAt"];
            if (createdAtToken is JValue { Value: DateTime parsedDateTime })
            {
                return parsedDateTime.Kind == DateTimeKind.Utc
                    ? parsedDateTime.ToLocalTime()
                    : parsedDateTime;
            }

            if (createdAtToken is JValue { Value: DateTimeOffset parsedDateTimeOffset })
                return parsedDateTimeOffset.LocalDateTime;

            if (DateTimeOffset.TryParse(
                    createdAtToken?.ToString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dateTimeOffset))
            {
                return dateTimeOffset.LocalDateTime;
            }

            return File.GetCreationTime(defaultPromptFile);
        }

        private void UseSelectedPrompt()
        {
            if (SelectedPrompt == null)
                return;

            _window.SelectedPrompt = SelectedPrompt.Content;
            _window.DialogResult = true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed record DemoPromptItem(
        string Title,
        string Content,
        DateTime CreatedAt);
}
