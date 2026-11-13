using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
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
    [Display(Name = "SftpDownloadFlowBlock_DisplayName", Description = "SftpDownloadFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class SftpDownloadFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.ByteArray;

        [Required]
        [Display(Name = "SftpFlowBlock_Connection", Description = "SftpFlowBlock_Connection_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleConnections), DisplayMember = nameof(Name))]
        public SftpConnectionProvider Connection { get; set; }

        [Required]
        [Display(Name = "SftpFlowBlock_RemotePath", Description = "SftpFlowBlock_RemotePath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public string RemotePath { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cloud_download_outline, 16, SKColors.Teal);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cloud_download_outline, 32, SKColors.Teal);
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Sftp;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public IEnumerable<SftpConnectionProvider> GetPossibleConnections()
            => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<SftpConnectionProvider>();

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Connection));
            properties.Add(nameof(RemotePath));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data) => Invoke(runtime, data, () =>
        {
            if (Connection == null) throw new ValidationException("No SFTP connection is configured.");
            var remotePath = this.Resolve(owner => owner.RemotePath, required: true).Trim();
            using var client = Connection.CreateClient();
            client.Connect();
            using var output = new MemoryStream();
            client.DownloadFile(remotePath, output);
            GenerateResult(runtime, Convert.ToBase64String(output.ToArray()));
        });
    }
}
