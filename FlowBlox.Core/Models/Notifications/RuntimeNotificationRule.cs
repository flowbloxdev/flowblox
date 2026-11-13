using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Notifications
{
    public sealed class RuntimeNotificationRule : FlowBloxReactiveObject
    {
        [Display(Name = "RuntimeNotificationRule_Type", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
        public RuntimeNotificationType Type { get; set; }

        [Display(Name = "RuntimeNotificationRule_Enabled", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool Enabled { get; set; }

        public override string ToString() => Type.ToString();
    }
}
