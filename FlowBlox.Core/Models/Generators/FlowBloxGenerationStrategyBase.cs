using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Generators
{
    public abstract class FlowBloxGenerationStrategyBase : FlowBloxReactiveObject
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.auto_fix, 16, SKColors.SeaGreen);

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.auto_fix, 32, SKColors.SeaGreen);

        protected FlowBloxGenerationStrategyBase()
        {
        }

        protected FlowBloxGenerationStrategyBase(BaseFlowBlock flowBlock)
        {
            Name = GetNameInContextOf(flowBlock);
            Source = flowBlock;
        }

        private string GetNameInContextOf(BaseFlowBlock flowBlock)
        {
            string baseName = GetType().Name;
            string name = baseName + "_0";
            int counter = 0;

            while (flowBlock.GenerationStrategies.Any(gs => gs.Name == name))
            {
                counter++;
                name = baseName + "_" + counter;
            }

            return name;
        }

        [Display(Name = "Global_Name", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string Name { get; set; }

        private BaseFlowBlock _source;

        public BaseFlowBlock Source
        {
            get
            {
                return _source;
            }
            set
            {
                _source = value;
                OnAfterSourceChanged();
            }
        }

        protected virtual void OnAfterSourceChanged()
        {
        }

        public abstract bool CanExecute(out Dictionary<FlowBloxTestDefinition, List<string>> testDefinitionToMessages, out List<string> messages);


        public abstract object Execute(BaseRuntime runtime, Dictionary<FlowBloxTestDefinition, FlowBloxTestResult> testResults);

        public abstract void Assign(object value);

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Name) ? base.ToString() : Name;
    }
}
