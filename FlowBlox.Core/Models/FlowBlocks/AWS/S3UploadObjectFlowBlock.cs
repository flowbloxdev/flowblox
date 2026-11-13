using FlowBlox.Core.Extensions;
using Amazon.S3.Model;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
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
    [Display(Name = "S3UploadObjectFlowBlock_DisplayName", Description = "S3UploadObjectFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class S3UploadObjectFlowBlock : BaseSingleResultFlowBlock
    {
        private FieldElement _contentField;

        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [Required]
        [Display(Name = "S3StorageFlowBlock_Provider", Description = "S3StorageFlowBlock_Provider_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleProviders), DisplayMember = nameof(AmazonWebServicesProvider.Name))]
        public AmazonWebServicesProvider Provider { get; set; }

        [Required]
        [Display(Name = "S3StorageFlowBlock_BucketName", Description = "S3StorageFlowBlock_BucketName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public string BucketName { get; set; }

        [Required]
        [Display(Name = "S3StorageFlowBlock_ObjectKey", Description = "S3StorageFlowBlock_ObjectKey_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public string ObjectKey { get; set; }

        [Required]
        [Display(Name = "S3UploadObjectFlowBlock_ContentField", Description = "S3UploadObjectFlowBlock_ContentField_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleFieldElements), DisplayMember = nameof(FieldElement.FullyQualifiedName))]
        public FieldElement ContentField
        {
            get => _contentField;
            set => SetRequiredInputField(ref _contentField, value);
        }

        [Display(Name = "S3UploadObjectFlowBlock_ContentType", Description = "S3UploadObjectFlowBlock_ContentType_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        public string ContentType { get; set; } = "application/octet-stream";

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cloud_upload_outline, 16, new SKColor(35, 92, 148));
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cloud_upload_outline, 32, new SKColor(35, 92, 148));

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

                var content = FlowBloxBinaryContentHelper.Resolve(ContentField, allowPlainText: true);
                using var contentStream = new MemoryStream(content);
                using var client = Provider.CreateS3Client();
                client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey,
                    InputStream = contentStream,
                    ContentType = this.Resolve(owner => owner.ContentType).Trim()
                }).GetAwaiter().GetResult();

                GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
            });
        }
    }
}
