using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Provider;
using System.Data;
using FlowBlox.Core.Constants;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Enums;
using System.Collections.ObjectModel;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.IO
{
    [FlowBloxUIGroup("TableWriterFlowBlock_Groups_Schema", 0)]
    [Display(Name = "TableWriterFlowBlock_DisplayName", Description = "TableWriterFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class TableWriterFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        private readonly Dictionary<string, DataRow> _rowKeyCache = new Dictionary<string, DataRow>();

        private ObservableCollection<TableColumnDefinition> _tableColumnDefinitions;
        private DataTable _currentTableData;
        private List<string> _keyColumns;
        private IReadableTable _subscribedReadableTable;
        private Action _readableTableDataSourceChangedListener;
        private BaseRuntime _deferredWriteRuntime;

        [Required()]
        [Display(Name = "TableWriterFlowBlock_ReferencedTable", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association, SelectionFilterMethod = nameof(GetPossibleWritableTables), SelectionDisplayMember = nameof(IWritableTable.Name))]
        public IWritableTable ReferencedTable { get; set; }

        [Display(Name = "TableWriterFlowBlock_CreateNewDatasets", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.Default)]
        public bool CreateNewDatasets { get; set; } = true;

        [Display(Name = "TableWriterFlowBlock_UpdateExistingDatasets", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Default)]
        public bool UpdateExistingDatasets { get; set; } = true;

        [Display(Name = "TableWriterFlowBlock_ClearOnRuntimeStart", Description = "TableWriterFlowBlock_ClearOnRuntimeStart_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(Factory = UIFactory.Default)]
        public bool ClearOnRuntimeStart { get; set; }

        [Display(Name = "TableWriterFlowBlock_WriteOnRuntimeFinished", Description = "TableWriterFlowBlock_WriteOnRuntimeFinished_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        public bool WriteOnRuntimeFinished { get; set; }

        public List<IWritableTable> GetPossibleWritableTables()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var tables = registry.GetManagedObjects<IWritableTable>();
            return tables.ToList();
        }

        [Display(Name = "TableWriterFlowBlock_TableColumnDefinitions", ResourceType = typeof(FlowBloxTexts), GroupName = "TableWriterFlowBlock_Groups_Schema", Order = 0)]
        [CustomValidation(typeof(TableWriterFlowBlock), nameof(ValidateTableColumnDefinitions))]
        [FlowBloxUI(Factory = UIFactory.GridView, DisplayLabel = false)]
        [FlowBloxDataGrid(IsMovable = true)]
        public ObservableCollection<TableColumnDefinition> TableColumnDefinitions
        {
            get
            {
                return _tableColumnDefinitions;
            }
            set
            {
                _tableColumnDefinitions = value;
            }
        }

        public static ValidationResult ValidateTableColumnDefinitions(ObservableCollection<TableColumnDefinition> tableColumnDefinitions, ValidationContext context)
        {
            var duplicateNames = tableColumnDefinitions
                .GroupBy(x => x.ColumnName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateNames.Any())
            {
                var errorMessage = string.Format(
                    FlowBloxResourceUtil.GetLocalizedString("TableWriterFlowBlock_ColumnAlreadyExists"), 
                    string.Join(", ", duplicateNames));

                return new ValidationResult(errorMessage, [context.MemberName]);
            }

            return ValidationResult.Success;
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.table_arrow_up, 16, SKColors.Orange);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.table_arrow_up, 32, SKColors.Orange);


        public TableWriterFlowBlock()
        {
            this.TableColumnDefinitions = new ObservableCollection<TableColumnDefinition>();
        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.IO;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(ReferencedTable));
            return properties;
        }

        private bool CanAppend(IList<TableColumnDefinition> columnDefinitions)
        {
            if (!columnDefinitions.Any(x => x.IsKeyColumn))
                return CreateNewDatasets;

            string rowKey = string.Join(GlobalConstants.KeyCacheSeparator, columnDefinitions
                .Where(x => x.IsKeyColumn)
                .Select(x => x.Field.Value));

            if (_rowKeyCache.ContainsKey(rowKey))
                return UpdateExistingDatasets;

            return CreateNewDatasets;
        }

        private string GetRowKey(DataRow row)
        {
            if (_keyColumns == null || !_keyColumns.Any())
                return null;

            var keyParts = new List<string>();
            foreach (var columnName in _keyColumns)
            {
                if (!_currentTableData.Columns.Contains(columnName))
                    throw new InvalidOperationException($"Unable to find key column \"{columnName}\" in current table data.");

                keyParts.Add(row[columnName].ToString());
            }
            return string.Join(GlobalConstants.KeyCacheSeparator, keyParts);
        }

        private void AppendRowToKeyCache(DataRow row)
        {
            var rowKey = GetRowKey(row);
            if (rowKey != null && !_rowKeyCache.ContainsKey(rowKey))
                _rowKeyCache.Add(rowKey, row);
        }

        private bool CreateOrUpdateDataRowFromCurrentSchemaDefinition(DataTable dataTable, IList<TableColumnDefinition> columnDefinitions)
        {
            if (!CanAppend(columnDefinitions))
                return false;

            _currentTableData = SyncSchemaAndDataTable(dataTable, columnDefinitions);
            DataRow dataRow = _currentTableData.NewRow();
            foreach (var columnDefinition in columnDefinitions)
            {
                dataRow[columnDefinition.ColumnName] = columnDefinition.Field.Value ?? DBNull.Value;
            }

            var rowKey = GetRowKey(dataRow);
            if (rowKey != null && _rowKeyCache?.TryGetValue(rowKey, out DataRow existingRow) == true)
            {
                // Update existing row
                foreach (var columnDefinition in columnDefinitions)
                {
                    existingRow[columnDefinition.ColumnName] = columnDefinition.Field.Value ?? DBNull.Value;
                }
            }
            else
            {
                // Create new row
                _currentTableData.Rows.Add(dataRow);
                AppendRowToKeyCache(dataRow);
            }

            return true;
        }

        private static DataTable SyncSchemaAndDataTable(DataTable dataTable, IList<TableColumnDefinition> columnDefinitions)
        {
            if (dataTable == null)
                dataTable = new DataTable();

            foreach (var columnDefinition in columnDefinitions)
            {
                if (!dataTable.Columns.Contains(columnDefinition.ColumnName))
                {
                    var newColumn = new DataColumn(columnDefinition.ColumnName, columnDefinition.Field.GetConfiguredType());
                    dataTable.Columns.Add(newColumn);

                    // Bestimme die Position der aktuellen Spalte in der Definitionsliste
                    int currentIndex = columnDefinitions.IndexOf(columnDefinition);

                    // Bestimme die Position, an der die Spalte eingefügt werden soll
                    int insertPosition = dataTable.Columns.Count - 1;

                    if (currentIndex > 0)
                    {
                        // Wenn es eine vorherige Spalte gibt, füge nach dieser Spalte ein
                        var previousColumnDefinition = columnDefinitions[currentIndex - 1];
                        if (dataTable.Columns.Contains(previousColumnDefinition.ColumnName))
                        {
                            insertPosition = dataTable.Columns.IndexOf(previousColumnDefinition.ColumnName) + 1;
                        }
                    }
                    else if (currentIndex < columnDefinitions.Count - 1)
                    {
                        // Wenn es eine nachfolgende Spalte gibt, versuche, vor dieser einzufügen
                        var nextColumnDefinition = columnDefinitions[currentIndex + 1];
                        if (dataTable.Columns.Contains(nextColumnDefinition.ColumnName))
                        {
                            insertPosition = dataTable.Columns.IndexOf(nextColumnDefinition.ColumnName);
                        }
                    }

                    // Aktualisiere die Position der neuen Spalte
                    dataTable.Columns[columnDefinition.ColumnName].SetOrdinal(insertPosition);
                }
            }

            return dataTable;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return this.Invoke(runtime, data, () => 
            {
                var hasWritten = CreateOrUpdateDataRowFromCurrentSchemaDefinition(_currentTableData, this.TableColumnDefinitions);
                if (hasWritten && !WriteOnRuntimeFinished)
                    this.ReferencedTable.Write(_currentTableData);

                if (hasWritten)
                    GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
                else
                    GenerateResult(runtime);
            });
        }

        private void InitRowKeyCache()
        {
            _rowKeyCache.Clear();

            if (_currentTableData == null)
                return;

            foreach (DataRow row in _currentTableData.Rows)
            {
                AppendRowToKeyCache(row);
            }
        }

        private void OnReadableTableInitializedOrChanged(IReadableTable readableTable, BaseRuntime runtime = null)
        {
            if (readableTable.CanRead(runtime))
            {
                this._currentTableData = ((IReadableTable)ReferencedTable).Read();
                InitRowKeyCache();
            }
            else
            {
                this._currentTableData = null;
                InitRowKeyCache();
            }
        }

        private void InitializeEmptyRuntimeTable()
        {
            _currentTableData = new DataTable();
            InitRowKeyCache();
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            _deferredWriteRuntime = null;
            this._keyColumns = this.TableColumnDefinitions
                .Where(x => x.IsKeyColumn)
                .Select(x => x.ColumnName)
                .ToList();

            if (ClearOnRuntimeStart)
                InitializeEmptyRuntimeTable();

            if (ReferencedTable is IReadableTable)
            {
                var readableTable = (IReadableTable)ReferencedTable;
                SubscribeReadableTableDataSourceChanged(readableTable, runtime);

                if (!ClearOnRuntimeStart)
                    OnReadableTableInitializedOrChanged(readableTable, runtime);
            }
            base.RuntimeStarted(runtime);
            if (WriteOnRuntimeFinished)
                _deferredWriteRuntime = runtime;
        }

        public override void RuntimeFinished(BaseRuntime runtime)
        {
            UnsubscribeReadableTableDataSourceChanged();
            var shouldWrite = _deferredWriteRuntime != null && ReferenceEquals(_deferredWriteRuntime, runtime);
            _deferredWriteRuntime = null;
            if (shouldWrite && !runtime.Aborted && _currentTableData != null)
                ReferencedTable.Write(SyncSchemaAndDataTable(_currentTableData, TableColumnDefinitions));
            base.RuntimeFinished(runtime);
        }

        private void SubscribeReadableTableDataSourceChanged(IReadableTable readableTable, BaseRuntime runtime)
        {
            UnsubscribeReadableTableDataSourceChanged();

            _subscribedReadableTable = readableTable;
            _readableTableDataSourceChangedListener = () => OnReadableTableInitializedOrChanged(readableTable, runtime);
            _subscribedReadableTable.AddDataSourceChangedListener(_readableTableDataSourceChangedListener);
        }

        private void UnsubscribeReadableTableDataSourceChanged()
        {
            if (_subscribedReadableTable == null || _readableTableDataSourceChangedListener == null)
                return;

            _subscribedReadableTable.RemoveDataSourceChangedListener(_readableTableDataSourceChangedListener);
            _subscribedReadableTable = null;
            _readableTableDataSourceChangedListener = null;
        }
    }
}
