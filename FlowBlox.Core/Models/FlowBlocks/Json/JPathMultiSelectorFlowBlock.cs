using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Json
{
    [Display(Name = "JPathMultiSelectorMappingEntry_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class JPathMultiSelectorMappingEntry : FlowBloxReactiveObject
    {
        private string _rowPath;
        private string _fallbackValue;

        [Required]
        [Display(Name = "JPathMultiSelectorMappingEntry_JPath", Description = "JPathMultiSelectorMappingEntry_JPath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.JPath))]
        public string JPath { get; set; }

        [JsonProperty("JsonPath")]
        private string LegacyJsonPath
        {
            set
            {
                if (string.IsNullOrWhiteSpace(JPath))
                    JPath = value;
            }
        }

        [Display(Name = "JPathMultiSelectorMappingEntry_RowPath", Description = "JPathMultiSelectorMappingEntry_RowPath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.JPath))]
        [CustomValidation(typeof(JPathRowPathValidator), nameof(JPathRowPathValidator.ValidateRowPath))]
        [DependsOnProperty(MemberName = nameof(FallbackValue))]
        public string RowPath
        {
            get => _rowPath;
            set
            {
                if (_rowPath == value)
                    return;

                _rowPath = value;
                OnPropertyChanged();
            }
        }

        [Display(Name = "JPathMultiSelectorMappingEntry_FallbackValue", Description = "JPathMultiSelectorMappingEntry_FallbackValue_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public string FallbackValue
        {
            get => _fallbackValue;
            set
            {
                if (_fallbackValue == value)
                    return;

                _fallbackValue = value;
                OnPropertyChanged();
            }
        }

        [Required]
        [Display(Name = "Global_FieldElement", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public FieldElement Field { get; set; }

        public override string ToString() =>
            !string.IsNullOrWhiteSpace(JPath) ? JPath : RowPath ?? string.Empty;
    }

    [FlowBloxUIGroup("JPathMultiSelectorFlowBlock_Groups_Mappings", 0)]
    [FlowBloxSpecialExplanation("JsonFlowBlocks_SpecialExplanation_JPathSyntax", Icon = SpecialExplanationIcon.Information)]
    [Display(Name = "JPathMultiSelectorFlowBlock_DisplayName", Description = "JPathMultiSelectorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class JPathMultiSelectorFlowBlock : BaseResultFlowBlock
    {
        [Required]
        [Display(Name = "JPathMultiSelectorFlowBlock_JsonContent", Description = "JPathMultiSelectorFlowBlock_JsonContent_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true, SyntaxHighlighting = "JSON")]
        public string JsonContent { get; set; }

        [Display(Name = "JPathMultiSelectorFlowBlock_MappingEntries", ResourceType = typeof(FlowBloxTexts), GroupName = "JPathMultiSelectorFlowBlock_Groups_Mappings", Order = 1)]
        [FlowBloxUI(DisplayLabel = false)]
        [FlowBloxDataGrid(
            ColumnMemberNames =
            [
                nameof(JPathMultiSelectorMappingEntry.JPath),
                nameof(JPathMultiSelectorMappingEntry.RowPath),
                nameof(JPathMultiSelectorMappingEntry.FallbackValue),
                nameof(JPathMultiSelectorMappingEntry.Field)
            ])]

        public ObservableCollection<JPathMultiSelectorMappingEntry> MappingEntries { get; set; } = new ObservableCollection<JPathMultiSelectorMappingEntry>();

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.selection_ellipse_arrow_inside, 16, SKColors.CadetBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.selection_ellipse_arrow_inside, 32, SKColors.CadetBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Json;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(JPathMultiSelectorNotifications));
                return notificationTypes;
            }
        }

        public override List<FieldElement> Fields
        {
            get
            {
                return MappingEntries
                    .Select(x => x.Field)
                    .ExceptNull()
                    .ToList();
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(JsonContent));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var jsonText = this.Resolve(owner => owner.JsonContent).Trim();
                if (string.IsNullOrWhiteSpace(jsonText))
                {
                    CreateNotification(runtime, JPathMultiSelectorNotifications.JsonContentIsEmpty);
                    GenerateResult(runtime);
                    return;
                }

                if (MappingEntries == null || MappingEntries.Count == 0)
                {
                    CreateNotification(runtime, JPathMultiSelectorNotifications.MappingEntriesAreEmpty);
                    GenerateResult(runtime);
                    return;
                }

                JToken rootToken;
                try
                {
                    // JSON strings remain strings; date parsing belongs to the target
                    // field type, otherwise timestamps lose their original offset/format.
                    using var reader = new JsonTextReader(new StringReader(jsonText))
                    {
                        DateParseHandling = DateParseHandling.None
                    };
                    rootToken = JToken.Load(reader);
                }
                catch (Exception)
                {
                    CreateNotification(runtime, JPathMultiSelectorNotifications.JsonContentInvalid);
                    GenerateResult(runtime);
                    return;
                }

                var resolvedMappings = new List<ResolvedMapping>();
                var hadUnresolvedPaths = false;

                foreach (var mappingEntry in MappingEntries)
                {
                    if (mappingEntry?.Field == null)
                        continue;

                    var jPath = mappingEntry.Resolve(owner => owner.JPath).Trim();
                    if (string.IsNullOrWhiteSpace(jPath))
                    {
                        hadUnresolvedPaths = true;
                        continue;
                    }

                    var rowPath = mappingEntry.Resolve(owner => owner.RowPath).Trim();
                    if (!string.IsNullOrWhiteSpace(rowPath))
                    {
                        try
                        {
                            var fallbackValue = mappingEntry.Resolve(owner => owner.FallbackValue);
                            if (!JPathRowValueResolver.TryResolve(rootToken, jPath, rowPath, fallbackValue, out var rowValues))
                            {
                                hadUnresolvedPaths = true;
                                continue;
                            }

                            resolvedMappings.Add(new ResolvedMapping(mappingEntry.Field, rowValues, true, fallbackValue));
                        }
                        catch (Exception)
                        {
                            hadUnresolvedPaths = true;
                        }

                        continue;
                    }

                    List<JToken> resultTokens;
                    try
                    {
                        resultTokens = rootToken
                            .SelectTokens(jPath, errorWhenNoMatch: false)
                            .ToList();
                    }
                    catch (Exception)
                    {
                        hadUnresolvedPaths = true;
                        continue;
                    }

                    if (resultTokens.Count == 0)
                    {
                        hadUnresolvedPaths = true;
                        continue;
                    }

                    var value = resultTokens.Count == 1
                        ? JPathTokenSerializer.Serialize(resultTokens[0])
                        : JPathTokenSerializer.Serialize(new JArray(resultTokens.Select(x => x.DeepClone())));
                    resolvedMappings.Add(new ResolvedMapping(mappingEntry.Field, [value], false, string.Empty));
                }

                if (hadUnresolvedPaths)
                    CreateNotification(runtime, JPathMultiSelectorNotifications.PathCouldNotBeResolved);

                if (resolvedMappings.Count == 0)
                {
                    CreateNotification(runtime, JPathMultiSelectorNotifications.ReturnedNoMatches);
                    GenerateResult(runtime);
                    return;
                }

                var rowCount = resolvedMappings
                    .Where(mapping => mapping.IsRowMapping)
                    .Select(mapping => mapping.Values.Count)
                    .DefaultIfEmpty(1)
                    .Max();
                var rows = Enumerable.Range(0, rowCount)
                    .Select(_ => new Dictionary<FieldElement, string>())
                    .ToList();

                foreach (var mapping in resolvedMappings)
                {
                    for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
                    {
                        rows[rowIndex][mapping.Field] = mapping.IsRowMapping
                            ? rowIndex < mapping.Values.Count ? mapping.Values[rowIndex] : mapping.FallbackValue
                            : mapping.Values[0];
                    }
                }

                GenerateResult(runtime, rows);
            });
        }

        private sealed record ResolvedMapping(
            FieldElement Field,
            IReadOnlyList<string> Values,
            bool IsRowMapping,
            string FallbackValue);

        public enum JPathMultiSelectorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "JSON content is empty")]
            JsonContentIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "No mapping entries configured")]
            MappingEntriesAreEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "JSON content is invalid")]
            JsonContentInvalid,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "One or more JSON paths could not be resolved")]
            PathCouldNotBeResolved,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "JSON path mapping returned no matches")]
            ReturnedNoMatches
        }
    }
}
