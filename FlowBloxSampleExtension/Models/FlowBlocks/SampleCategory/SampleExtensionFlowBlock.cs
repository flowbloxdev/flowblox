using FlowBlox.Core.Constants;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using FlowBloxSampleExtension.Models.Components;
using FlowBlox.Core.Models.Base;
using System.Collections.ObjectModel;

namespace FlowBloxSampleExtension.Models.FlowBlocks.SampleCategory
{
    /// <summary>
    /// Defines one replacement rule for <see cref="SampleExtensionFlowBlock"/>.
    /// </summary>
    /// <remarks>
    /// This type derives from <see cref="FlowBloxReactiveObject"/> and is intended to be used as
    /// a nested configuration object inside a parent FlowBlox component (for example a FlowBlock or ManagedObject).
    /// In this sample, instances are owned by <see cref="SampleExtensionFlowBlock.ValueReplacements"/>.
    ///
    /// Reactive objects are context-bound:
    /// 1. They are serialized/deserialized as part of the owning parent object.
    /// 2. They are not globally managed/registered as standalone project components.
    /// 3. They can be created, edited, linked, or removed only through the current parent context.
    /// </remarks>
    [Display(Name = "SampleExtensionMappingEntry_DisplayName", ResourceType = typeof(SampleExtensionResources))]
    public class SampleExtensionMappingEntry : FlowBloxReactiveObject
    {
        /// <summary>
        /// The source value to search for in the resolved output text.
        /// Supports field placeholders via field selection UI.
        /// </summary>
        [Display(Name = "SampleExtensionMappingEntry_SourceValue", ResourceType = typeof(SampleExtensionResources), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string SourceValue { get; set; }

        /// <summary>
        /// The value that replaces <see cref="SourceValue"/> when a match is found.
        /// Supports field placeholders via field selection UI.
        /// </summary>
        [Display(Name = "SampleExtensionMappingEntry_ReplacementValue", ResourceType = typeof(SampleExtensionResources), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string ReplacementValue { get; set; }
    }

    /// <summary>
    /// Demo flow block that outputs a text value and applies configured replacement mappings.
    /// </summary>
    [Display(Name = "SampleExtensionFlowBlock_DisplayName", Description = "SampleExtensionFlowBlock_Description", ResourceType = typeof(SampleExtensionResources))]
    public class SampleExtensionFlowBlock : BaseSingleResultFlowBlock
    {
        /// <summary>
        /// Input/output text template for this flow block.
        /// Field placeholders are resolved at runtime before the result is generated.
        /// </summary>
        [Display(Name = "SampleExtensionFlowBlock_OutputText", ResourceType = typeof(SampleExtensionResources), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        [Required()]
        public string OutputText { get; set; }

        /// <summary>
        /// Ordered list of replacement entries applied to the resolved <see cref="OutputText"/>.
        /// </summary>
        /// <remarks>
        /// This property demonstrates a GridView configuration backed by reactive child objects.
        /// The list is part of this flow block's configuration and does not exist independently.
        /// </remarks>
        [Display(Name = "SampleExtensionFlowBlock_ValueReplacements", Description = "SampleExtensionFlowBlock_ValueReplacements_Tooltip", ResourceType = typeof(SampleExtensionResources), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        [FlowBloxDataGrid]
        public ObservableCollection<SampleExtensionMappingEntry> ValueReplacements { get; set; } = new();

        /// <summary>
        /// Small toolbox/property icon (16x16).
        /// </summary>
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(SampleExtensionResources.cube, 16, SKColors.DarkSlateBlue);

        /// <summary>
        /// Large toolbox/property icon (32x32).
        /// </summary>
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(SampleExtensionResources.cube, 32, SKColors.DarkSlateBlue);

        /// <summary>
        /// Initializes default values after the instance has been created.
        /// </summary>
        public override void OnAfterCreate()
        {
            OutputText = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.SampleExtensionFlowBlockDefaultOutputText].Value.ToString();
            base.OnAfterCreate();
        }

        /// <summary>
        /// Test hook for sample extension development.
        /// </summary>
        public void Test()
        {

        }

        /// <summary>
        /// The block can consume multiple input datasets.
        /// </summary>
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        /// <summary>
        /// Returns the custom category used by this sample extension.
        /// </summary>
        public override FlowBlockCategory GetCategory() => FlowBloxSampleCategories.Sample;

        /// <summary>
        /// Defines the properties shown in the FlowBlox property UI.
        /// </summary>
        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(OutputText));
            properties.Add(nameof(ValueReplacements));
            return properties;
        }

        /// <summary>
        /// Executes the flow block and emits one result value.
        /// </summary>
        /// <param name="runtime">Current runtime instance.</param>
        /// <param name="data">Parent dataset/context object.</param>
        /// <returns><c>true</c> when invocation was processed by the common invoke pipeline.</returns>
        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                // On failure, create the notification and emit an empty result.
                if (!this.ResolveOrCreateNotification(
                    x => x.OutputText,
                    out var outputText,
                    runtime,
                    SampleExtensionFlowBlockNotifications.OutputTextIsEmpty))
                    return;

                // Apply configured replacement entries in their current list order.
                // Each mapping entry is also field-aware (SourceValue/ReplacementValue).
                foreach (var mapping in ValueReplacements ?? Enumerable.Empty<SampleExtensionMappingEntry>())
                {
                    if (mapping == null)
                        continue;

                    if (!mapping.TryResolve(x => x.SourceValue, out var sourceValue))
                        continue;

                    var replacementValue = mapping.Resolve(x => x.ReplacementValue);
                    outputText = outputText.Replace(sourceValue, replacementValue, StringComparison.Ordinal);
                }

                GenerateResult(runtime, outputText);
            });
        }

        /// <summary>
        /// Registers notification enums used by this flow block.
        /// </summary>
        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(SampleExtensionFlowBlockNotifications));
                return notificationTypes;
            }
        }

        /// <summary>
        /// Notification messages emitted by <see cref="SampleExtensionFlowBlock"/>.
        /// </summary>
        public enum SampleExtensionFlowBlockNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Output text is empty")]
            OutputTextIsEmpty
        }

        /// <summary>
        /// Provides extension-specific default options.
        /// </summary>
        /// <param name="defaults">Mutable option list that is filled during registration.</param>
        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.Add(new OptionElement("SampleExtensionFlowBlock.DefaultOutputText", "This is a default output text.", "Defines the default output text for the sample FlowBlock.", OptionElement.OptionType.Text));
            base.OptionsInit(defaults);
        }
    }
}
