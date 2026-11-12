using SkiaSharp;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Fields;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes.FlowBlox.Core.Attributes;

namespace FlowBlox.Core.Models.Components.IO
{
    [Display(Name = "FileObject_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("FileObject_DisplayName_Plural", typeof(FlowBloxTexts))]
    public class FileObject : DataObjectBase
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_document_outline, 16, new SKColor(2, 132, 199));

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_document_outline, 32, new SKColor(2, 132, 199));
        [Required()]
        [Display(Name = "PropertyNames_FilePath", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFileSelection)]
        [FlowBloxUIFileSelection("All files (*.*)|*.*")]
        public string FilePath { get; set; }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public override byte[] Content
        {
            get
            {
                var filePath = GetRuntimeFilePath();
                return File.Exists(filePath) ? 
                    File.ReadAllBytes(filePath) : 
                    null;
            }
            set
            {
                var filePath = GetRuntimeFilePath();
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllBytes(filePath, value);
                    TriggerDataSourceChanged();
                }
            }
        }

        public string GetRuntimeFilePath() => GetRuntimeFilePath(FilePath);

        private string GetRuntimeFilePath(string filePath)
        {
            var resolvedPath = FlowBloxFieldHelper.ReplaceFieldsInString(filePath);
            if (string.IsNullOrWhiteSpace(resolvedPath))
                return string.Empty;

            return IOUtil.NormalizePath(resolvedPath);
        }

        public override bool CanRead()
        {
            return File.Exists(GetRuntimeFilePath());
        }

        public override List<string> GetDisplayableProperties()
            => [nameof(Name), nameof(FilePath)];

        public override void RegisterPropertyChangedEventHandlers()
        {
            foreach (var fieldElement in FlowBloxFieldHelper.GetFieldElementsFromString(FilePath))
            {
                fieldElement.OnNameChanged += FieldElement_NameChange;
                fieldElement.OnValueChanged += FieldElement_ValueChange;
            }
        }

        private void FieldElement_ValueChange(FieldElement field, string oldValue, string newValue)
        {
            TriggerDataSourceChanged();
        }

        private void FieldElement_NameChange(FieldElement field, string oldName, string newName)
        {
            FilePath = FilePath.Replace(oldName, newName);
        }

        public override string ToString()
        {
            return $"From file: \"{FilePath}\"";
        }
    }
}



