using FlowBlox.Core.Extensions;
using Amazon.S3.Model;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.AWS
{
    [Display(Name = "S3DownloadObjectFlowBlock_DisplayName", Description = "S3DownloadObjectFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class S3DownloadObjectFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.ByteArray;

        [Required]
        [Display(Name = "S3StorageFlowBlock_Provider", Description = "S3StorageFlowBlock_Provider_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association,
            SelectionFilterMethod = nameof(GetPossibleProviders),
            SelectionDisplayMember = nameof(AmazonWebServicesProvider.Name))]
        public AmazonWebServicesProvider Provider { get; set; }

        [Required]
        [Display(Name = "S3StorageFlowBlock_BucketName", Description = "S3StorageFlowBlock_BucketName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string BucketName { get; set; }

        [Required]
        [Display(Name = "S3StorageFlowBlock_ObjectKey", Description = "S3StorageFlowBlock_ObjectKey_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string ObjectKey { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cloud_download_outline, 16, new SKColor(35, 92, 148));
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cloud_download_outline, 32, new SKColor(35, 92, 148));

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.S3Storage;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public IEnumerable<AmazonWebServicesProvider> GetPossibleProviders()
            => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<AmazonWebServicesProvider>();

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Provider));
            properties.Add(nameof(BucketName));
            properties.Add(nameof(ObjectKey));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (Provider == null)
                    throw new InvalidOperationException("No AWS provider is configured.");

                var bucketName = this.Resolve(owner => owner.BucketName).Trim();
                var objectKey = this.Resolve(owner => owner.ObjectKey).Trim();
                if (string.IsNullOrWhiteSpace(bucketName) || string.IsNullOrWhiteSpace(objectKey))
                    throw new InvalidOperationException("S3 bucket name and object key are required.");

                using var client = Provider.CreateS3Client();
                using var response = client.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey
                }).GetAwaiter().GetResult();
                using var outputStream = new MemoryStream();
                response.ResponseStream.CopyTo(outputStream);

                GenerateResult(runtime, Convert.ToBase64String(outputStream.ToArray()));
            });
        }
    }
}
