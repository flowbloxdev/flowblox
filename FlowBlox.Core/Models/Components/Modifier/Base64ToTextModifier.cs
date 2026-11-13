using FlowBlox.Core.Extensions;
using System.Text;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.Fields;
using global::FlowBlox.Core.Extensions;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Components.Modifier
{
    [Display(Name = "Base64ToTextModifier_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class Base64ToTextModifier : ModifierBase
    {
        [Required]
        [Display(Name = "PropertyNames_EncodingName", Description = "PropertyNames_EncodingName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public DotNetEncodingNames EncodingName { get; set; }

        public override string Modify(BaseRuntime runtime, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                Encoding encoding = this.Resolve(modifier => modifier.EncodingName).ToEncoding();
                return encoding.GetString(bytes);
            }
            catch (FormatException ex)
            {
                runtime.Report($"Invalid Base64 input", FlowBloxLogLevel.Error, ex);
                return string.Empty;
            }
            catch (Exception ex)
            {
                runtime.Report($"Error during Base64 decoding", FlowBloxLogLevel.Error, ex);
                return string.Empty;
            }
        }

        public override string ToString()
        {
            var format = FlowBloxResourceUtil.GetLocalizedString(nameof(Base64ToTextModifier), nameof(ObjectDisplayName));
            return string.Format(format, this.Resolve(x => x.EncodingName));
        }
    }
}

