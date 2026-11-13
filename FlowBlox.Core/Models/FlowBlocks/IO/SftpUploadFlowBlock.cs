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

namespace FlowBlox.Core.Models.FlowBlocks.IO
{
    [Display(Name = "SftpUploadFlowBlock_DisplayName", Description = "SftpUploadFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class SftpUploadFlowBlock : BaseSingleResultFlowBlock
    {
        private FieldElement _contentField;
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [Required]
        [Display(Name = "SftpFlowBlock_Connection", Description = "SftpFlowBlock_Connection_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleConnections), DisplayMember = nameof(Name))]
        public SftpConnectionProvider Connection { get; set; }

        [Required]
        [Display(Name = "SftpFlowBlock_RemotePath", Description = "SftpFlowBlock_RemotePath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public string RemotePath { get; set; }

        [Required]
        [Display(Name = "SftpUploadFlowBlock_ContentField", Description = "SftpUploadFlowBlock_ContentField_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleFieldElements), DisplayMember = nameof(FieldElement.FullyQualifiedName))]
        public FieldElement ContentField { get => _contentField; set => SetRequiredInputField(ref _contentField, value); }

        [Display(Name = "SftpUploadFlowBlock_Overwrite", Description = "SftpUploadFlowBlock_Overwrite_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public bool Overwrite { get; set; } = true;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cloud_upload_outline, 16, SKColors.Teal);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cloud_upload_outline, 32, SKColors.Teal);
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Sftp;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public IEnumerable<SftpConnectionProvider> GetPossibleConnections()
            => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<SftpConnectionProvider>();

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Connection));
            properties.Add(nameof(RemotePath));
            properties.Add(nameof(ContentField));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data) => Invoke(runtime, data, () =>
        {
            if (Connection == null) throw new ValidationException("No SFTP connection is configured.");
            var remotePath = this.Resolve(owner => owner.RemotePath, required: true).Trim();
            var content = FlowBloxBinaryContentHelper.Resolve(ContentField, allowPlainText: true);
            using var input = new MemoryStream(content, writable: false);
            using var client = Connection.CreateClient();
            client.Connect();
            client.UploadFile(input, remotePath, this.Resolve(x => x.Overwrite));
            GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
        });
    }
}
