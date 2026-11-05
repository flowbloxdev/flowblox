using FlowBlox.Core.Constants;
using SkiaSharp;
using FlowBlox.Core.Util.Resources;
using System.Data;
using FlowBlox.Core.Util;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;

namespace FlowBlox.Core.Models.Components.IO
{
    [Display(Name = "CsvTable_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("CsvTable_DisplayName_Plural", typeof(FlowBloxTexts))]
    public class CsvTable : DataTableBase
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_delimited_outline, 16, new SKColor(37, 99, 235));

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_delimited_outline, 32, new SKColor(37, 99, 235));
        [Required()]
        [Display(Name = "PropertyNames_EncodingName", Description = "PropertyNames_EncodingName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.ComboBox)]
        public DotNetEncodingNames EncodingName { get; set; }

        [Required()]
        [Display(Name = "CsvTable_Separator", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public string Separator { get; set; }

        public CsvTable() : base()
        {
            this.EncodingName = DotNetEncodingNames.Default;
        }

        public override void OnAfterCreate()
        {
            // Separator
            this.Separator = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.CsvTableCellSeparator].Value;

            base.OnAfterCreate();
        }

        public override DataTable Read()
        {
            if (DataSource == null || !DataSource.CanRead())
                throw new InvalidOperationException("The data source is not ready for reading.");

            var dataTableConverter = new DataTableConverter(Separator, EncodingName.ToEncoding());
            using (var memoryStream = new MemoryStream(DataSource.Content))
            {
                return dataTableConverter.GetDataTableFromCsv(memoryStream, FirstRowHeader);
            }
        }

        public override void Write(DataTable dataTable)
        {
            if (DataSource == null)
                throw new InvalidOperationException("The data source is not ready for writing.");

            var dataTableConverter = new DataTableConverter(Separator, EncodingName.ToEncoding());
            using (var memoryStream = new MemoryStream())
            {
                dataTableConverter.DataTableToCsv(memoryStream, dataTable);
                DataSource.Content = memoryStream.ToArray();
            }
        }

        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.Add(new OptionElement("CsvTable.CellSeparator", ";", "Defines the list delimiter used during table export/import.", OptionElement.OptionType.Text));
        }
    }
}



