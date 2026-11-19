using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.Knowledge;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.Knowledge;

[Display(Name = "SearchKnowledgeFlowBlock_DisplayName", Description = "SearchKnowledgeFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
public sealed class SearchKnowledgeFlowBlock : BaseSingleResultCollectorFlowBlock
{
    [Required]
    [Display(Name = "VectorTable_DisplayName", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    [FlowBloxAssociation]
    [FlowBloxItemsSource(Method = nameof(GetTables), DisplayMember = nameof(Name))]
    public VectorTable Table { get; set; }

    [Required]
    [Display(Name = "UpsertVectorSourceFlowBlock_SourceId", ResourceType = typeof(FlowBloxTexts), Order = 1)]
    public string SourceId { get; set; }

    [Required]
    [Display(Name = "SearchKnowledgeFlowBlock_IndexStatus", ResourceType = typeof(FlowBloxTexts), Order = 2)]
    [FlowBloxAssociation]
    [FlowBloxItemsSource(Method = nameof(GetPossibleFieldElements), DisplayMember = nameof(FieldElement.FullyQualifiedName))]
    public FieldElement IndexStatus { get; set; }

    [Range(1000, 200000)]
    [Display(Name = "SearchKnowledgeFlowBlock_MaxEvidenceCharacters", Description = "SearchKnowledgeFlowBlock_MaxEvidenceCharacters_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
    public int MaxEvidenceCharacters { get; set; } = 24000;

    public IEnumerable<VectorTable> GetTables() => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<VectorTable>();
    public override FlowBlockCategory GetCategory() => FlowBlockCategory.Persistence;
    public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
    public override string DefaultResultFieldName => "Evidence";
    public override List<string> GetDisplayableProperties() => [.. base.GetDisplayableProperties(), nameof(Table)];
    public override List<Type> NotificationTypes => [.. base.NotificationTypes, typeof(SearchNotifications)];
    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.magnify_scan, 16, SKColors.MediumPurple);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.magnify_scan, 32, SKColors.MediumPurple);

    public override bool Execute(BaseRuntime runtime, object data)
    {
        return Invoke(runtime, data, () =>
        {
            var sourceId = this.Resolve(block => block.SourceId);
            if (!string.IsNullOrWhiteSpace(sourceId))
                StageValue(IndexStatus.Value is true ? sourceId : "\0index-failed");
            if (!CanGenerateResult())
                return;

            if (StagedValues.Count == 0)
            {
                GenerateResult(runtime);
                return;
            }

            if (StagedValues.Contains("\0index-failed"))
            {
                CreateNotification(runtime, SearchNotifications.IndexIncomplete);
                GenerateResult(runtime);
                return;
            }

            try
            {
                var active = StagedValues.ToHashSet(StringComparer.Ordinal);
                var matches = Table.Read(active);
                var evidence = FormatEvidence(matches, this.Resolve(block => block.MaxEvidenceCharacters));
                if (string.IsNullOrEmpty(evidence))
                {
                    CreateNotification(runtime, SearchNotifications.NoMatches);
                    GenerateResult(runtime);
                    return;
                }

                GenerateResult(runtime, evidence);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && exception is not ExecutionEnvironmentPrerequisiteException)
            {
                runtime.Report("Knowledge retrieval failed.", FlowBloxLogLevel.Error, exception);
                CreateNotification(runtime, SearchNotifications.SearchFailed, exception);
                GenerateResult(runtime);
            }
        });
    }

    private static string FormatEvidence(DataTable matches, int maximum)
    {
        var text = new StringBuilder();
        var citation = 1;
        foreach (DataRow row in matches.Rows)
        {
            var header = $"[{citation}] {row["Title"]}{Environment.NewLine}Source: {row["SourceId"]}{Environment.NewLine}"
                + "Similarity: " + ((double)row["Score"]).ToString("F4", CultureInfo.InvariantCulture) + Environment.NewLine;
            var remaining = maximum - text.Length - header.Length - 2 * Environment.NewLine.Length;
            if (remaining <= 0)
                break;

            var content = (string)row["Text"];
            text.Append(header);
            text.Append(content.AsSpan(0, Math.Min(remaining, content.Length)));
            text.AppendLine();
            text.AppendLine();
            citation++;
        }

        return text.ToString();
    }

    public enum SearchNotifications
    {
        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Source indexing incomplete")]
        IndexIncomplete,

        [FlowBloxNotification(NotificationType = NotificationType.Warning)]
        [Display(Name = "No matching knowledge chunks")]
        NoMatches,

        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Knowledge search failed")]
        SearchFailed
    }
}
