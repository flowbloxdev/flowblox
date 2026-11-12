using System.ComponentModel.DataAnnotations;
using System.Web;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Models.Components.Modifier
{
    public enum HttpEncodeMode
    {
        [Display(Name = "HTML")]
        HTML,
        [Display(Name = "URL")]
        URL,
        [Display(Name = "JavaScript")]
        JavaScript
    }

    public enum Direction
    {
        [Display(Name = "Direction_Encode", ResourceType = typeof(FlowBloxTexts))]
        Encode,
        [Display(Name = "Direction_Decode", ResourceType = typeof(FlowBloxTexts))]
        Decode
    }

    [Display(Name = "HttpEncodeDecodeModifier_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class HttpEncodeDecodeModifier : ModifierBase
    {
        [Required]
        [Display(Name = "HttpEncodeDecodeModifier_Mode", Order = 0, ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public HttpEncodeMode Mode { get; set; }

        [Required]
        [Display(Name = "HttpEncodeDecodeModifier_Direction", Order = 1, ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public Direction Direction { get; set; }

        public override string Modify(BaseRuntime runtime, string value)
        {
            var mode = this.Resolve(modifier => modifier.Mode);
            switch (mode)
            {
                case HttpEncodeMode.HTML:
                    return ModifyHtml(value);
                case HttpEncodeMode.URL:
                    return ModifyUrl(value);
                case HttpEncodeMode.JavaScript:
                    return ModifyJavaScript(runtime, value);
                default:
                    throw new NotImplementedException($"Unsupported mode: {mode}");
            }
        }

        private string ModifyHtml(string value)
        {
            if (this.Resolve(modifier => modifier.Direction) == Direction.Encode)
                return HttpUtility.HtmlEncode(value);
            else
                return HttpUtility.HtmlDecode(value);
        }

        private string ModifyUrl(string value)
        {
            if (this.Resolve(modifier => modifier.Direction) == Direction.Encode)
                return HttpUtility.UrlEncode(value);
            else
                return HttpUtility.UrlDecode(value);
        }

        private string ModifyJavaScript(BaseRuntime runtime, string value)
        {
            if (this.Resolve(modifier => modifier.Direction) == Direction.Encode)
                return HttpUtility.JavaScriptStringEncode(value);
            else
            {
                runtime.Report("Decoding is not applied for JavaScript values as it is typically unnecessary.", FlowBloxLogLevel.Info);
                return value;
            }
        }

        public override string ToString()
        {
            var format = FlowBloxResourceUtil.GetLocalizedString(nameof(HttpEncodeDecodeModifier), nameof(ObjectDisplayName));
            return string.Format(format, this.Mode.GetDisplayName(), this.Direction.GetDisplayName());
        }
    }
}

