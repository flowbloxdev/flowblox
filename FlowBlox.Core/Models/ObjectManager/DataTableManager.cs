using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Interfaces;
using System.Collections.ObjectModel;
using FlowBlox.Core.Extensions;

namespace FlowBlox.Core.Models.ObjectManager
{
    [Display(Name = "DataTableManager_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [UIMetadataDefinitions(typeof(FlowBloxIcons), nameof(FlowBloxIcons.table_arrow_down), "#0D9488", 16)]
    [FlowBloxUIGroup(Name = "DataTableManager_Groups_CsvTables", ControlAlignment = ControlAlignment.Fill)]
    [FlowBloxUIGroup(Name = "DataTableManager_Groups_SqlTables", ControlAlignment = ControlAlignment.Fill)]
    [FlowBloxUIGroup(Name = "DataTableManager_Groups_ExcelTables", ControlAlignment = ControlAlignment.Fill)]
    public class DataTableManager : IDockableObjectManager
    {
        private FlowBloxRegistry _registry;

        public bool IsActive => true;

        public DataTableManager()
        {
            CsvTables = new ObservableCollection<CsvTable>();
            SqlTables = new ObservableCollection<SQLTable>();
            ExcelTables = new ObservableCollection<ExcelTable>();
        }

        public DataTableManager(FlowBloxRegistry registry) : this()
        {
            _registry = registry;

            Reload();
        }

        public void Reload()
        {
            CsvTables.Clear();
            CsvTables.AddRange(_registry.GetManagedObjects<CsvTable>());

            SqlTables.Clear();
            SqlTables.AddRange(_registry.GetManagedObjects<SQLTable>());

            ExcelTables.Clear();
            ExcelTables.AddRange(_registry.GetManagedObjects<ExcelTable>());
        }

        [Display(ResourceType = typeof(FlowBloxTexts), GroupName = "DataTableManager_Groups_CsvTables")]
        [FlowBloxUI(DisplayLabel = false)]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(CsvTable.Name), nameof(CsvTable.DataSource) })]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public ObservableCollection<CsvTable> CsvTables { get; set; }

        [Display(ResourceType = typeof(FlowBloxTexts), GroupName = "DataTableManager_Groups_SqlTables")]
        [FlowBloxUI(DisplayLabel = false)]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(SQLTable.Name), nameof(SQLTable.DbType) })]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public ObservableCollection<SQLTable> SqlTables { get; set; }

        [Display(ResourceType = typeof(FlowBloxTexts), GroupName = "DataTableManager_Groups_ExcelTables")]
        [FlowBloxUI(DisplayLabel = false)]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(ExcelTable.Name), nameof(ExcelTable.DataSource) })]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public ObservableCollection<ExcelTable> ExcelTables { get; set; }
    }
}
