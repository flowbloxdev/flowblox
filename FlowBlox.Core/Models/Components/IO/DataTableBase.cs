using SkiaSharp;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace FlowBlox.Core.Models.Components.IO
{
    [Display(Name = "TypeNames_DataTable", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("TypeNames_DataTable_Plural", typeof(FlowBloxTexts))]
    public abstract class DataTableBase : ManagedObject, IReadableTable, IWritableTable
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.table_large, 16, new SKColor(21, 101, 192));

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.table_large, 32, new SKColor(21, 101, 192));
        [Required]
        [Display(Name = "PropertyNames_DataSource", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleDataSources), DisplayMember = nameof(DataObjectBase.Name))]
        public DataObjectBase DataSource { get; set; }

        protected IEnumerable<DataObjectBase> GetPossibleDataSources()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var dataSources = registry.GetManagedObjects<DataObjectBase>();
            return dataSources;
        }

        [Display(Name = "DataTableBase_FirstRowHeader", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public bool FirstRowHeader { get; set; } = true;

        protected DataTableBase()
        {
            this.FirstRowHeader = true;
        }

        public override List<string> GetDisplayableProperties()
            => [nameof(Name), nameof(DataSource)];

        public bool CanRead(BaseRuntime runtime = null)
        {
            return DataSource.CanRead();
        }

        public void AddDataSourceChangedListener(Action value)
        {
            DataSource.AddDataSourceChangedListener(value);
        }

        public void RemoveDataSourceChangedListener(Action value)
        {
            DataSource.RemoveDataSourceChangedListener(value);
        }

        public abstract DataTable Read();

        public abstract void Write(DataTable dataTable);
    }
}
