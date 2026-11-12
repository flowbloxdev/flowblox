using FlowBlox.Core.Extensions;
using SkiaSharp;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace FlowBlox.Core.Models.Components.IO
{
    [Serializable()]
    [Display(Name = "SQLTable_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("SQLTable_DisplayName_Plural", typeof(FlowBloxTexts))]
    public class SQLTable : ManagedObject, IReadableTable
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_cog, 16, new SKColor(220, 38, 38));

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_cog, 32, new SKColor(220, 38, 38));

        private List<Action> _readableTableDataSourceChangedHandler = new List<Action>();

        [Required()]
        [Display(Name = "SQLTable_DbType",
                 Description = "SQLTable_DbType_Tooltip",
                 ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public DbTypes DbType { get; set; }

        [Required()]
        [Display(Name = "SQLTable_SQLConnectionstring",
                 Description = "SQLTable_SQLConnectionstring_Tooltip",
                 ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Default, ToolboxCategory = nameof(FlowBloxToolboxCategory.DBConnection), UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true)]
        public string SQLConnectionstring { get; set; }

        [Required()]
        [Display(Name = "SQLTable_SQLStatement",
                 Description = "SQLTable_SQLStatement_Tooltip",
                 ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true, SyntaxHighlighting = "SQL")]
        public string SQLStatement { get; set; }

        [Display(Name = "SQLTable_UseFieldValuesAsParameters",
                 Description = "SQLTable_UseFieldValuesAsParameters_Tooltip",
                 ResourceType = typeof(FlowBloxTexts), Order = 4)]
        public bool UseFieldValuesAsParameters { get; set; } = true;

        public bool CanRead(BaseRuntime runtime = null)
        {
            if (this.TryRead(out _, runtime))
                return true;

            return false;
        }

        public override void RegisterPropertyChangedEventHandlers()
        {
            var referencedFields = FlowBloxFieldHelper.GetFieldElementsFromString(SQLConnectionstring)
                .Concat(FlowBloxFieldHelper.GetFieldElementsFromString(SQLStatement))
                .Distinct();
            foreach (FieldElement fieldElement in referencedFields)
            {
                fieldElement.OnValueChanged += new FieldElement.FieldElementValueChangedEventHandler(FieldElement_ValueChange);
            }

            base.RegisterPropertyChangedEventHandlers();
        }

        public void AddDataSourceChangedListener(Action listener)
        {
            if (listener == null || _readableTableDataSourceChangedHandler.Contains(listener))
                return;

            _readableTableDataSourceChangedHandler.Add(listener);
        }

        public void RemoveDataSourceChangedListener(Action listener)
        {
            if (listener == null)
                return;

            _readableTableDataSourceChangedHandler.Remove(listener);
        }

        private void FieldElement_ValueChange(FieldElement field, string oldValue, string newValue)
        {
            foreach (var handler in _readableTableDataSourceChangedHandler.ToList())
                handler.Invoke();
        }

        public DataTable Read()
        {
            var sqlConnectionstring = this.Resolve(owner => owner.SQLConnectionstring);
            var dbType = this.Resolve(table => table.DbType);
            var dbConnection = DbConnectionProvider.Instance.GetOrCreateDbConnection(dbType, sqlConnectionstring);
            if (dbConnection != null)
            {
                Dictionary<string, object> parameters;
                var sqlStatement = FlowBloxFieldHelper.ReplaceFieldsInSQL(
                    SQLStatement,
                    dbType,
                    out parameters,
                    useFieldValuesAsParameters: UseFieldValuesAsParameters);
                return DbConnectionUtil.GetOutputAsDataTable(sqlStatement, parameters, dbConnection);
            }
            throw new InvalidOperationException($"Unable to connect to \"{SQLConnectionstring}\".");
        }

        private bool TryRead(out DataTable dataTable, BaseRuntime runtime = null)
        {
            try
            {
                dataTable = this.Read();
                return true;
            }
            catch (Exception exception)
            {
                runtime?.Report($"SQLTable \"{Name}\": failed to execute read query.", FlowBloxLogLevel.Error, exception);
                dataTable = null;
                return false;
            }
        }

        public override List<string> GetDisplayableProperties()
            => [nameof(Name), nameof(DbType)];

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            _readableTableDataSourceChangedHandler.Clear();
        }
    }
}
