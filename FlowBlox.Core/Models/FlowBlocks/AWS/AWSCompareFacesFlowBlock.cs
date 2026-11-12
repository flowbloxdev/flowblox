using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace FlowBlox.Core.Models.FlowBlocks.AWS
{
    [FlowBloxUIGroup("AWSCompareFacesFlowBlock_Groups_InputImages", 0)]
    [FlowBloxUIGroup("AWSCompareFacesFlowBlock_Groups_ResultFields", 1)]
    [Display(
        Name = "AWSCompareFacesFlowBlock_DisplayName",
        Description = "AWSCompareFacesFlowBlock_Description",
        ResourceType = typeof(FlowBloxTexts))]
    public sealed class AWSCompareFacesFlowBlock : BaseResultFlowBlock
    {
        private FieldElement _comparisonImage;

        [Required]
        [Display(Name = "AWSCompareFacesFlowBlock_Provider", Description = "AWSCompareFacesFlowBlock_Provider_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(
            Factory = UIFactory.Association,
            SelectionFilterMethod = nameof(GetPossibleProviders),
            SelectionDisplayMember = nameof(AmazonWebServicesProvider.Name))]
        public AmazonWebServicesProvider Provider { get; set; }

        [Required]
        [Display(Name = "AWSCompareFacesFlowBlock_ComparisonImage", Description = "AWSCompareFacesFlowBlock_ComparisonImage_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(
            Factory = UIFactory.Association,
            SelectionFilterMethod = nameof(GetPossibleFieldElements),
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            Operations = UIOperations.Link | UIOperations.Unlink)]
        public FieldElement ComparisonImage
        {
            get => _comparisonImage;
            set => SetRequiredInputField(ref _comparisonImage, value);
        }

        [Range(0d, 100d)]
        [Display(Name = "AWSCompareFacesFlowBlock_SimilarityThreshold", Description = "AWSCompareFacesFlowBlock_SimilarityThreshold_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public double SimilarityThreshold { get; set; } = 90d;

        [Display(Name = "AWSCompareFacesFlowBlock_QualityFilter", Description = "AWSCompareFacesFlowBlock_QualityFilter_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public AWSCompareFacesQualityFilter QualityFilter { get; set; } = AWSCompareFacesQualityFilter.Auto;

        [Range(1, 1000)]
        [Display(Name = "AWSCompareFacesFlowBlock_MaxResults", Description = "AWSCompareFacesFlowBlock_MaxResults_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int MaxResults { get; set; } = 100;

        [Display(Name = "AWSCompareFacesFlowBlock_InputImageMappings", Description = "AWSCompareFacesFlowBlock_InputImageMappings_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AWSCompareFacesFlowBlock_Groups_InputImages", Order = 0)]
        [FlowBloxUI(Factory = UIFactory.GridView, DisplayLabel = false)]
        [FlowBloxDataGrid(GridColumnMemberNames =
            [
                nameof(InputImageMappingEntry.FieldElement),
                nameof(InputImageMappingEntry.Key)
            ])]
        public ObservableCollection<InputImageMappingEntry> InputImageMappings { get; set; } = new();

        [Display(Name = "AWSCompareFacesFlowBlock_ResultFields", Description = "AWSCompareFacesFlowBlock_ResultFields_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AWSCompareFacesFlowBlock_Groups_ResultFields", Order = 0)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        public ObservableCollection<ResultFieldByEnumValue<AWSCompareFacesDestinations>> ResultFields { get; set; } = new();

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.magnify_scan, 16, new SKColor(35, 92, 148));

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.magnify_scan, 32, new SKColor(35, 92, 148));

        public override List<FieldElement> Fields
        {
            get
            {
                return ResultFields
                    .Where(x => x.EnumValue != null)
                    .Select(x => x.ResultField)
                    .ExceptNull()
                    .ToList();
            }
        }

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.AI;

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Provider));
            properties.Add(nameof(ComparisonImage));
            properties.Add(nameof(SimilarityThreshold));
            properties.Add(nameof(QualityFilter));
            properties.Add(nameof(MaxResults));
            properties.Add(nameof(InputImageMappings));
            return properties;
        }

        public IEnumerable<AmazonWebServicesProvider> GetPossibleProviders()
        {
            return FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<AmazonWebServicesProvider>();
        }

        public override void OnAfterCreate()
        {
            CreateDefaultResultFields();
            base.OnAfterCreate();
        }

        private void CreateDefaultResultFields()
        {
            if (ResultFields.Any())
                return;

            CreateDestinationResultField(ResultFields, AWSCompareFacesDestinations.PersonKey);
            CreateDestinationResultField(ResultFields, AWSCompareFacesDestinations.Similarity, FieldTypes.Double);
            CreateDestinationResultField(ResultFields, AWSCompareFacesDestinations.MatchCount, FieldTypes.Integer);
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (Provider == null)
                    throw new InvalidOperationException("No AWS provider configured.");

                if (ComparisonImage == null)
                    throw new InvalidOperationException("No comparison image configured.");

                if (InputImageMappings == null || InputImageMappings.Count == 0)
                {
                    CreateNotification(runtime, AWSCompareFacesNotifications.NoInputImageMappings);
                    GenerateResult(runtime);
                    return;
                }

                if (!ResultFields.Any())
                    throw new InvalidOperationException("No result fields have been configured.");

                var targetImage = ResolveImageBytes(ComparisonImage);
                var matches = new List<CompareFacesMatch>();

                foreach (var mapping in InputImageMappings.Where(x => x?.FieldElement != null))
                {
                    var key = mapping.Resolve(owner => owner.Key).Trim();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var sourceImage = ResolveImageBytes(mapping.FieldElement);
                    var response = CompareFaces(
                        sourceImage,
                        targetImage,
                        SimilarityThreshold,
                        QualityFilter);

                    var bestSimilarity = response.FaceMatches?
                        .Select(x => x?.Similarity ?? 0f)
                        .DefaultIfEmpty(0f)
                        .Max() ?? 0f;

                    if (bestSimilarity >= SimilarityThreshold)
                    {
                        matches.Add(new CompareFacesMatch(
                            PersonKey: key,
                            Similarity: bestSimilarity));
                    }
                }

                if (matches.Count == 0)
                {
                    CreateNotification(runtime, AWSCompareFacesNotifications.NoFacesMatched);
                    GenerateResult(runtime);
                    return;
                }

                var limitedMatches = matches
                    .OrderByDescending(x => x.Similarity)
                    .Take(Math.Max(1, MaxResults))
                    .ToList();

                var matchCount = limitedMatches.Count.ToString(CultureInfo.InvariantCulture);
                var results = limitedMatches.Select(match =>
                    new ResultFieldByEnumValueResultBuilder<AWSCompareFacesDestinations>()
                        .For(AWSCompareFacesDestinations.PersonKey, match.PersonKey)
                        .For(AWSCompareFacesDestinations.Similarity, match.Similarity.ToString(CultureInfo.InvariantCulture))
                        .For(AWSCompareFacesDestinations.MatchCount, matchCount)
                        .Build(ResultFields));

                GenerateResult(runtime, results);
            });
        }

        private static byte[] ResolveImageBytes(FieldElement field)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            if (field.GetConfiguredType() == typeof(byte[]))
                return field.Value as byte[] ?? Array.Empty<byte>();

            var value = field.Resolve(owner => owner.StringValue).Trim();
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<byte>();

            if (File.Exists(value))
                return File.ReadAllBytes(value);

            try
            {
                return Convert.FromBase64String(value);
            }
            catch (FormatException ex)
            {
                throw new FormatException($"Image field '{field.FullyQualifiedName}' must contain a ByteArray/Base64 image or an existing file path.", ex);
            }
        }

        private Amazon.Rekognition.Model.CompareFacesResponse CompareFaces(
            byte[] sourceImage,
            byte[] targetImage,
            double similarityThreshold,
            AWSCompareFacesQualityFilter qualityFilter)
        {
            if (sourceImage == null || sourceImage.Length == 0)
                throw new ArgumentException("Source image is empty.", nameof(sourceImage));

            if (targetImage == null || targetImage.Length == 0)
                throw new ArgumentException("Target image is empty.", nameof(targetImage));

            using var client = Provider.CreateClient<AmazonRekognitionClient>();
            using var sourceStream = new MemoryStream(sourceImage);
            using var targetStream = new MemoryStream(targetImage);

            var request = new CompareFacesRequest
            {
                SourceImage = new Image { Bytes = sourceStream },
                TargetImage = new Image { Bytes = targetStream },
                SimilarityThreshold = (float)Math.Clamp(similarityThreshold, 0d, 100d),
                QualityFilter = ToRekognitionQualityFilter(qualityFilter)
            };

            return client.CompareFacesAsync(request).GetAwaiter().GetResult();
        }

        private static Amazon.Rekognition.QualityFilter ToRekognitionQualityFilter(AWSCompareFacesQualityFilter qualityFilter)
        {
            return qualityFilter switch
            {
                AWSCompareFacesQualityFilter.Auto => Amazon.Rekognition.QualityFilter.AUTO,
                AWSCompareFacesQualityFilter.Low => Amazon.Rekognition.QualityFilter.LOW,
                AWSCompareFacesQualityFilter.Medium => Amazon.Rekognition.QualityFilter.MEDIUM,
                AWSCompareFacesQualityFilter.High => Amazon.Rekognition.QualityFilter.HIGH,
                _ => Amazon.Rekognition.QualityFilter.NONE
            };
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(AWSCompareFacesNotifications));
                return notificationTypes;
            }
        }

        private sealed record CompareFacesMatch(string PersonKey, float Similarity);

        public enum AWSCompareFacesNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "AWSCompareFacesFlowBlock_Notifications_NoInputImageMappings", ResourceType = typeof(FlowBloxTexts))]
            NoInputImageMappings,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "AWSCompareFacesFlowBlock_Notifications_NoFacesMatched", ResourceType = typeof(FlowBloxTexts))]
            NoFacesMatched
        }
    }
}
