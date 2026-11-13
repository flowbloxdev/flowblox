using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Base;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    [Display(Name = "MediaToolBase_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("MediaToolBase_DisplayName_Plural", typeof(FlowBloxTexts))]
    public abstract class MediaToolBase : ManagedObject
    {
        [Display(Name = "MediaToolBase_BinaryDirectory", Description = "MediaToolBase_BinaryDirectory_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.FLD | UISelectionButtons.DI)]
        public string BinaryDirectory { get; set; } = string.Empty;

        protected abstract string ExecutableName { get; }

        public string GetExecutablePath()
        {
            var directory = ResolveBinaryDirectory();
            return string.IsNullOrWhiteSpace(directory) ? ExecutableName : Path.Combine(directory, ExecutableName);
        }

        protected string ResolveBinaryDirectory()
        {
            var directory = this.Resolve(tool => tool.BinaryDirectory).Trim();
            return string.IsNullOrWhiteSpace(directory) ? string.Empty : Path.GetFullPath(directory);
        }
    }
}
