using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Web.InternalWebRequest
{
    public class WebRequestParameter : FlowBloxReactiveObject
    {
        [Display(Name = "WebRequestParameter_Key", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        public string Key { get; set; }

        [Display(Name = "WebRequestParameter_Value", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public string Value { get; set; }

        public override string ToString() => Key ?? string.Empty;
    }
}
