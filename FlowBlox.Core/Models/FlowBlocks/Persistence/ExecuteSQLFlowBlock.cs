using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Persistence
{
    [Display(Name = "ExecuteSQLFlowBlock_DisplayName", Description = "ExecuteSQLFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class ExecuteSQLFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => FlowBlox.Core.Constants.GlobalConstants.SuccessFieldName;

        [Display(Name = "ExecuteSQLFlowBlock_DbType", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [Required]
        public DbTypes DbType { get; set; }

        [Display(Name = "ExecuteSQLFlowBlock_ConnectionString", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.DBConnection))]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true)]
        [Required]
        public string SQLConnectionstring { get; set; }

        [Display(Name = "ExecuteSQLFlowBlock_SQLStatement", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.SQL))]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true, SyntaxHighlighting = "FlowBlox.UICore.Resources.Highlighting.SQL.xshd")]
        [Required]
        public string SQLStatement { get; set; }

        [Display(Name = "ExecuteSQLFlowBlock_UseFieldValuesAsParameters",
                 Description = "ExecuteSQLFlowBlock_UseFieldValuesAsParameters_Tooltip",
                 ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool UseFieldValuesAsParameters { get; set; } = true;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_cog, 16, SKColors.IndianRed);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_cog, 32, SKColors.IndianRed);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Persistence;

        public int ExecuteSQL(DbTypes dbType, string sqlConnectionstring, string sqlStatement)
        {
            var resolvedConnectionString = FlowBloxFieldHelper.ReplaceFieldsInString(sqlConnectionstring);
            var dbConnection = DbConnectionProvider.Instance.GetOrCreateDbConnection(dbType, resolvedConnectionString);
            if (dbConnection != null)
            {
                Dictionary<string, object> parameters;
                sqlStatement = FlowBloxFieldHelper.ReplaceFieldsInSQL(
                    sqlStatement,
                    dbType,
                    out parameters,
                    useFieldValuesAsParameters: UseFieldValuesAsParameters);
                return DbConnectionUtil.ExecuteSqlQuery(sqlStatement, parameters, dbConnection);
            }
            throw new InvalidOperationException($"Unable to connect to \"{sqlConnectionstring}\".");
        }

        public int ExecuteSQL()
        {
            return ExecuteSQL(this.Resolve(x => x.DbType), SQLConnectionstring, SQLStatement);
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(DbType));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var updatedRows = ExecuteSQL();
                runtime.Report($"Successfully executed SQL Query: Updated row count is {updatedRows}");
                GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
            });
        }
    }
}
