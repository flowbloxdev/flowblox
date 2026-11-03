using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Microsoft.Win32;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FlowBlox.Core.Models.FlowBlocks.IO
{
    [Display(Name = "FSDirectoryIteratorFlowBlock_DisplayName", Description = "FSDirectoryIteratorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class FSDirectoryIteratorFlowBlock : BaseResultFlowBlock
    {
        [Required]
        [Display(Name = "FSDirectoryIteratorFlowBlock_DirectoryPath", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFolderSelection | UIOptions.EnableFieldSelection)]
        public string DirectoryPath { get; set; }

        [Display(Name = "FSDirectoryIteratorFlowBlock_Recursive", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool Recursive { get; set; }
        public FieldElement Recursive_SelectedField { get; set; }

        [Display(Name = "FSDirectoryIteratorFlowBlock_FilterExpression", Description = "FSDirectoryIteratorFlowBlock_FilterExpression_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public string FilterExpression { get; set; }

        [Display(Name = "FSDirectoryIteratorFlowBlock_ResultFields", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        public ObservableCollection<ResultFieldByEnumValue<FSDirectoryIteratorDestinations>> ResultFields { get; set; }

        public FSDirectoryIteratorFlowBlock()
        {
            ResultFields = new ObservableCollection<ResultFieldByEnumValue<FSDirectoryIteratorDestinations>>();
            Recursive = true;
            FilterExpression = "*";
        }

        public override void OnAfterCreate()
        {
            CreateDefaultResultFields();
            base.OnAfterCreate();
        }

        private void CreateDefaultResultFields()
        {
            CreateDestinationResultField(ResultFields, FSDirectoryIteratorDestinations.FullPath);
        }

        public override List<FieldElement> Fields
        {
            get
            {
                return ResultFields
                    .Where(x => x.EnumValue != null)
                    .Select(x => x.ResultField)
                    .ExceptNull()
                    .ToList();
            }
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.folder_search_outline, 16, new SKColor(14, 116, 144));
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.folder_search_outline, 32, new SKColor(14, 116, 144));

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.IO;

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(FSDirectoryIteratorNotifications));
                return notificationTypes;
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(DirectoryPath));
            properties.Add(nameof(Recursive));
            properties.Add(nameof(FilterExpression));
            return properties;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Replace('\\', '/');
        }

        private static bool WildcardMatch(string input, string pattern)
        {
            var normalizedInput = NormalizePath(input);
            var normalizedPattern = NormalizePath(pattern ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedPattern))
                return false;

            var regexBuilder = new StringBuilder();
            regexBuilder.Append('^');

            foreach (var c in normalizedPattern)
            {
                switch (c)
                {
                    case '*':
                        regexBuilder.Append(".*");
                        break;
                    case '?':
                        regexBuilder.Append('.');
                        break;
                    default:
                        regexBuilder.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }

            regexBuilder.Append('$');
            return Regex.IsMatch(normalizedInput, regexBuilder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private bool IsMatch(string relativePath, string fileName)
        {
            var resolvedFilter = FlowBloxFieldHelper.ReplaceFieldsInString(FilterExpression ?? string.Empty);
            if (string.IsNullOrWhiteSpace(resolvedFilter))
                return true;

            var tokens = resolvedFilter
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (!tokens.Any())
                return true;

            var includes = tokens.Where(x => !x.StartsWith("!", StringComparison.Ordinal)).ToList();
            var excludes = tokens
                .Where(x => x.StartsWith("!", StringComparison.Ordinal))
                .Select(x => x.Substring(1).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            bool MatchesAny(string pattern) =>
                WildcardMatch(relativePath, pattern) || WildcardMatch(fileName, pattern);

            var includeMatches = !includes.Any() || includes.Any(MatchesAny);
            if (!includeMatches)
                return false;

            var excludeMatches = excludes.Any(MatchesAny);
            return !excludeMatches;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (!ResultFields.Any())
                    throw new InvalidOperationException("No result fields have been configured.");

                var directoryPath = FlowBloxFieldHelper.ReplaceFieldsInString(DirectoryPath ?? string.Empty);
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    CreateNotification(runtime, FSDirectoryIteratorNotifications.DirectoryPathIsEmpty);
                    GenerateResult(runtime);
                    return;
                }

                if (!Directory.Exists(directoryPath))
                {
                    CreateNotification(runtime, FSDirectoryIteratorNotifications.DirectoryDoesNotExist);
                    GenerateResult(runtime);
                    return;
                }

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(this, x => x.Recursive),
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false
                };

                var resultMap = new List<Dictionary<FieldElement, string>>();
                foreach (var fullPath in Directory.EnumerateFiles(directoryPath, "*", options))
                {
                    var fileName = Path.GetFileName(fullPath);
                    var relativePath = Path.GetRelativePath(directoryPath, fullPath);

                    if (!IsMatch(relativePath, fileName))
                        continue;

                    var fileInfo = new FileInfo(fullPath);
                    var row = new ResultFieldByEnumValueResultBuilder<FSDirectoryIteratorDestinations>()
                        .For(FSDirectoryIteratorDestinations.FullPath, fileInfo.FullName)
                        .For(FSDirectoryIteratorDestinations.RelativePath, NormalizePath(relativePath))
                        .For(FSDirectoryIteratorDestinations.FileName, fileName)
                        .For(FSDirectoryIteratorDestinations.Size, fileInfo.Length.ToString(CultureInfo.InvariantCulture))
                        .For(FSDirectoryIteratorDestinations.LastModified, fileInfo.LastWriteTime.ToString("o", CultureInfo.InvariantCulture))
                        .For(FSDirectoryIteratorDestinations.CreationDate, fileInfo.CreationTime.ToString("o", CultureInfo.InvariantCulture))
                        .Build(ResultFields);

                    resultMap.Add(row);
                }

                if (resultMap.Count == 0)
                    CreateNotification(runtime, FSDirectoryIteratorNotifications.NoFilesFound);

                GenerateResult(runtime, resultMap);
            });
        }

        public enum FSDirectoryIteratorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Directory path is empty")]
            DirectoryPathIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Directory does not exist")]
            DirectoryDoesNotExist,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "No files found")]
            NoFilesFound
        }
    }
}
