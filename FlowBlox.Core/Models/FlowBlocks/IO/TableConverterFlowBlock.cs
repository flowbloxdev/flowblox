using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.IO
{
    [Display(Name = "TableConverterFlowBlock_DisplayName", Description = "TableConverterFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public sealed class TableConverterFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [Required]
        [Display(Name = "TableConverterFlowBlock_SourceTable", Description = "TableConverterFlowBlock_SourceTable_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleSourceTables), DisplayMember = nameof(IReadableTable.Name))]
        public IReadableTable SourceTable { get; set; }

        [Required]
        [Display(Name = "TableConverterFlowBlock_TargetTable", Description = "TableConverterFlowBlock_TargetTable_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleTargetTables), DisplayMember = nameof(IWritableTable.Name))]
        public IWritableTable TargetTable { get; set; }

        public List<IReadableTable> GetPossibleSourceTables() =>
            FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<IReadableTable>().ToList();

        public List<IWritableTable> GetPossibleTargetTables() =>
            FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<IWritableTable>().ToList();

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.table_large, 16, SKColors.MediumPurple);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.table_large, 32, SKColors.MediumPurple);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Conversion;

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (SourceTable == null)
                    throw new InvalidOperationException("No source table has been configured.");
                if (TargetTable == null)
                    throw new InvalidOperationException("No target table has been configured.");
                if (!SourceTable.CanRead(runtime))
                    throw new InvalidOperationException($"Source table '{SourceTable.Name}' is not ready to read.");

                var dataTable = SourceTable.Read();
                TargetTable.Write(dataTable);
                GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
            });
        }
    }
}
