using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.Fields;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Generation
{
    [Display(
        Name = "IndexFlowBlock_DisplayName",
        Description = "IndexFlowBlock_Description",
        ResourceType = typeof(FlowBloxTexts))]
    public class IndexFlowBlock : BaseSingleResultFlowBlock
    {
        private int _currentIndex;
        private bool _initialized;

        [Display(Name = "IndexFlowBlock_InitialIndex", Description = "IndexFlowBlock_InitialIndex_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        public int InitialIndex { get; set; } = 0;

        /// <summary>
        /// If set, the index resets to InitialIndex whenever this contextual flowblock starts a new iteration.
        /// If not set, the index is global for the whole runtime execution (never reset).
        /// </summary>
        [Display(Name = "IndexFlowBlock_Context", Description = "IndexFlowBlock_Context_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleContextFlowBlocks), DisplayMember = nameof(Name))]
        public BaseFlowBlock AssociatedContextualObject { get; set; }

        public IEnumerable<BaseFlowBlock> GetPossibleContextFlowBlocks()
        {
            return FlowBloxRegistryProvider.GetRegistry().GetFlowBlocks()
                .Where(x => x.Name != Name)
                .ToList();
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.identifier, 16, SKColors.DarkSlateBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.identifier, 32, SKColors.DarkSlateBlue);

        public IndexFlowBlock() : base() 
        {

        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Generation;

        public override FieldTypes DefaultResultFieldType => FieldTypes.Integer;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(InitialIndex));
            properties.Add(nameof(AssociatedContextualObject));
            return properties;
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            base.RuntimeStarted(runtime);
            ResetCurrentIndex(runtime);
            // Test/project input values may be assigned after initialization; read them on first execution.
            _initialized = false;

            if (AssociatedContextualObject != null)
            {
                AssociatedContextualObject.IterationStart -= Context_IterationStart;
                AssociatedContextualObject.IterationStart += Context_IterationStart;

                runtime.Report($"IndexFlowBlock \"{Name}\": Index resets on IterationStart of context \"{AssociatedContextualObject.Name}\". InitialIndex={_currentIndex}");
            }
            else
            {
                runtime.Report($"IndexFlowBlock \"{Name}\": No context set -> index is global for whole runtime. InitialIndex={_currentIndex}");
            }
        }

        public override void RuntimeFinished(BaseRuntime runtime)
        {
            if (AssociatedContextualObject != null)
                AssociatedContextualObject.IterationStart -= Context_IterationStart;

            base.RuntimeFinished(runtime);
        }

        private void Context_IterationStart(BaseRuntime runtime)
        {
            ResetCurrentIndex(runtime);
        }

        private void ResetCurrentIndex(BaseRuntime runtime)
        {
            _currentIndex = this.Resolve(block => block.InitialIndex);
            _initialized = true;

            runtime.Report($"IndexFlowBlock \"{Name}\": Reset index to InitialIndex={_currentIndex}");
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _currentIndex = this.Resolve(block => block.InitialIndex);
            _initialized = true;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                EnsureInitialized();

                int value;
                value = _currentIndex;
                _currentIndex++;

                GenerateResult(runtime, value.ToString());
            });
        }
    }
}
