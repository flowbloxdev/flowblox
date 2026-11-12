using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace FlowBlox.Core.Models.FlowBlocks.Additions
{
    [Display(Name = "LogicalGroupCondition_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class LogicalGroupCondition : LogicalCondition
    {
        [JsonIgnore]
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.vector_arrange_below, 16, new SKColor(96, 125, 139));

        [JsonIgnore]
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.vector_arrange_below, 32, new SKColor(96, 125, 139));

        public LogicalGroupCondition()
        {
            Conditions = new ObservableCollection<LogicalCondition>();
        }

        [Display(Name = "LogicalGroupCondition_Conditions", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(
            Factory = UIFactory.GridView,
            CreatableTypes = new[] { 
                typeof(FieldLogicalComparisonCondition), 
                typeof(LogicalGroupCondition) })]
        [FlowBloxDataGrid(GridColumnMemberNames = [
            nameof(LogicalCondition.LogicalOperator),
            nameof(LogicalCondition.DisplayName)
            ])]
        public ObservableCollection<LogicalCondition> Conditions { get; set; }

        public override string DisplayName
        {
            get
            {
                if (Conditions == null || Conditions.Count == 0)
                    return FlowBloxTexts.LogicalGroupCondition_DisplayName;

                string JoinLogicalOperator(LogicalOperator op) =>
                    op == LogicalOperator.And ? "and" : "or";

                string WrapIfGroup(LogicalCondition condition)
                {
                    if (condition is LogicalGroupCondition)
                        return $"({condition.DisplayName})";

                    return condition.DisplayName;
                }

                var parts = Conditions
                    .Where(c => c != null)
                    .Select((c, index) =>
                    {
                        var name = WrapIfGroup(c);

                        if (index == 0)
                            return name;

                        return $"{JoinLogicalOperator(c.LogicalOperator)} {name}";
                    })
                    .ToList();

                if (parts.Count == 0)
                    return FlowBloxTexts.LogicalGroupCondition_DisplayName;

                return string.Join(" ", parts);
            }
        }

        public override string ShortDisplayName => DisplayName;

        public override bool Check()
        {
            // Semantics:
            // - The first condition is taken as the initial result.
            // - Each subsequent condition's LogicalOperator connects it with the accumulated result.
            // - LogicalOperator is a property of the condition item (not of the parent/group).
            // - Nested LogicalGroupCondition is evaluated by calling its Check() recursively.

            if (Conditions == null || Conditions.Count == 0)
                return true;

            bool? result = null;

            foreach (var condition in Conditions.Where(c => c != null))
            {
                bool current = condition.Check();

                if (result == null)
                {
                    result = current;
                    continue;
                }

                switch (condition.LogicalOperator)
                {
                    case LogicalOperator.And:
                        result = result.Value && current;
                        break;

                    case LogicalOperator.Or:
                        result = result.Value || current;
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported logical operator: {condition.LogicalOperator}");
                }
            }

            return result ?? true;
        }
    }
}