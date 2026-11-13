using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace FlowBlox.Core.Models.Components.Modifier
{
    [Display(Name = "HmacModifier_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class HmacModifier : ModifierBase
    {
        [Display(Name = "HmacModifier_Algorithm", Description = "HmacModifier_Algorithm_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        public CryptographicHashAlgorithm Algorithm { get; set; } = CryptographicHashAlgorithm.SHA256;

        [Required]
        [Display(Name = "HmacModifier_SecretKey", Description = "HmacModifier_SecretKey_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public string SecretKey { get; set; }

        [Display(Name = "HmacModifier_KeyEncoding", Description = "HmacModifier_KeyEncoding_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public CryptographicKeyEncoding KeyEncoding { get; set; } = CryptographicKeyEncoding.Text;

        [Display(Name = "HashModifier_OutputEncoding", Description = "HashModifier_OutputEncoding_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public CryptographicOutputEncoding OutputEncoding { get; set; } = CryptographicOutputEncoding.HexLowerCase;

        public override string Modify(BaseRuntime runtime, string value)
        {
            var secretKey = this.Resolve(owner => owner.SecretKey);
            var keyEncoding = this.Resolve(modifier => modifier.KeyEncoding);
            var algorithm = this.Resolve(modifier => modifier.Algorithm);
            var outputEncoding = this.Resolve(modifier => modifier.OutputEncoding);
            var key = CryptographicModifierSupport.DecodeKey(secretKey, keyEncoding);
            using var hmac = CryptographicModifierSupport.CreateHmac(algorithm, key);
            return CryptographicModifierSupport.Encode(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)),
                outputEncoding);
        }

        public override string ToString() => string.Format(
            FlowBloxResourceUtil.GetLocalizedString(nameof(HmacModifier), nameof(ObjectDisplayName)),
            Algorithm.GetDisplayName(), OutputEncoding.GetDisplayName());
    }
}
