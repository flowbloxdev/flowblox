using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Compression
{
    [Display(Name = "ZipArchiveCreatorFlowBlock_DisplayName", Description = "ZipArchiveCreatorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class ZipArchiveCreatorFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [Required]
        [Display(Name = "ZipArchiveCreatorFlowBlock_ZipArchiveObject", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association,
                     SelectionFilterMethod = nameof(GetPossibleZipArchiveObjects),
                     SelectionDisplayMember = nameof(Name))]
        public ZipArchiveObject ZipArchiveObject { get; set; }

        private IEnumerable<ZipArchiveObject> GetPossibleZipArchiveObjects()
        {
            return FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<ZipArchiveObject>();
        }

        [Display(Name = "ZipArchiveCreatorFlowBlock_CompressionStrength", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public ZipCompressionStrength CompressionStrength { get; set; }

        [Display(Name = "ZipArchiveCreatorFlowBlock_Password", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox]
        public string Password { get; set; }

        public ZipArchiveCreatorFlowBlock()
        {
            CompressionStrength = ZipCompressionStrength.Medium;
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_export, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_export, 32, SKColors.SteelBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Compression;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(ZipArchiveObject));
            properties.Add(nameof(CompressionStrength));
            properties.Add(nameof(Password));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (ZipArchiveObject == null)
                    throw new InvalidOperationException("No ZIP archive object is assigned.");

                try
                {
                    var password = this.Resolve(owner => owner.Password);
                    ZipArchiveObject.CreateNewArchive(CompressionStrength, password);
                    GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
                }
                catch (Exception e)
                {
                    runtime.Report(e.ToString());
                    CreateNotification(runtime, ZipArchiveCreatorNotifications.FailedToCreateArchive);
                    GenerateResult(runtime);
                }
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(ZipArchiveCreatorNotifications));
                return notificationTypes;
            }
        }

        public enum ZipArchiveCreatorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Failed to create ZIP archive")]
            FailedToCreateArchive
        }
    }
}
