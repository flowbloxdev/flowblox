using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.ControlFlow
{
    public abstract class RecursiveTargetFlowBlockBase : BaseFlowBlock
    {
        private BaseFlowBlock _targetFlowBlock;

        [Required]
        [Display(Name = "RecursiveTargetFlowBlockBase_TargetFlowBlock", Description = "RecursiveTargetFlowBlockBase_TargetFlowBlock_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [CustomValidation(typeof(RecursiveTargetFlowBlockBase), nameof(ValidateTargetFlowBlock))]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleTargetFlowBlocks), DisplayMember = nameof(Name))]
        public BaseFlowBlock TargetFlowBlock
        {
            get => _targetFlowBlock;
            set
            {
                if (ReferenceEquals(_targetFlowBlock, value))
                    return;
                _targetFlowBlock = value;
                OnTargetFlowBlockChanged();
                if (IsLoaded)
                    NotifyComponentChanged();
            }
        }

        protected virtual void OnTargetFlowBlockChanged() { }

        public List<BaseFlowBlock> GetPossibleTargetFlowBlocks()
        {
            var result = new List<BaseFlowBlock>();
            var visited = new HashSet<BaseFlowBlock>();
            var queue = new Queue<BaseFlowBlock>(ReferencedFlowBlocks.Where(x => x != null));
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current))
                    continue;
                result.Add(current);
                foreach (var previous in current.ReferencedFlowBlocks.Where(x => x != null))
                    queue.Enqueue(previous);
            }
            return result.Where(x => !ReferenceEquals(x, this)).Distinct().OrderBy(x => x.Name).ToList();
        }

        public static ValidationResult ValidateTargetFlowBlock(BaseFlowBlock target, ValidationContext context)
        {
            if (target == null)
                return ValidationResult.Success;
            var block = (RecursiveTargetFlowBlockBase)context.ObjectInstance;
            return block.GetPossibleTargetFlowBlocks().Contains(target)
                ? ValidationResult.Success
                : new ValidationResult(FlowBloxResourceUtil.GetLocalizedString("RecursiveTargetFlowBlockBase_Validation_TargetNotReachable"), [context.MemberName]);
        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
        public override bool CanBeReferenced => false;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.ControlFlow;

        public override void RuntimeStarted(Runtime.BaseRuntime runtime)
        {
            base.RuntimeStarted(runtime);
            var registry = FlowBloxRegistryProvider.GetRegistry();
            if (registry == null)
                return;
            var previousFlowBlocks = GetAllPreviousFlowBlocks(this, registry);
            var sources = registry.GetFlowBlocks().OfType<BaseFlowBlock>()
                .Where(x => x.IterationContext != null && previousFlowBlocks.Contains(x.IterationContext)).Distinct().ToList();
            if (sources.Count == 0)
                return;
            var names = string.Join(", ", sources.Select(x => $"\"{x.Name}\"").OrderBy(x => x));
            runtime.Report($"A recursion is executed within an iteration context. This means the source flow block(s) {names} are only executed after all results, including recursive results, are available.", FlowBloxLogLevel.Warning);
        }

        private static HashSet<BaseFlowBlock> GetAllPreviousFlowBlocks(BaseFlowBlock flowBlock, FlowBloxRegistry registry)
        {
            var result = new HashSet<BaseFlowBlock>();
            var queue = new Queue<BaseFlowBlock>(registry.GetPreviousElements(flowBlock).OfType<BaseFlowBlock>());
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == null || !result.Add(current))
                    continue;
                foreach (var previous in registry.GetPreviousElements(current).OfType<BaseFlowBlock>())
                    if (previous != null) queue.Enqueue(previous);
            }
            return result;
        }
    }

    [Display(Name = "InvocationFieldTransferConfig", ResourceType = typeof(FlowBloxTexts))]
    public class InvocationFieldTransferConfig : FlowBloxReactiveObject
    {
        [Required]
        [Display(Name = "InvocationFieldTransferConfig_TransferFrom", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxComboBox]
        [FlowBloxItemsSource(
            DisplayMember = nameof(FieldElement.FullyQualifiedName),
            Method = nameof(InjectionFlowBlock.GetPossibleTransferFromFieldElements),
            Dependency = nameof(InjectionFlowBlock.ReferencedFlowBlocks))]
        public FieldElement TransferFrom { get; set; }

        [Required]
        [Display(Name = "InvocationFieldTransferConfig_TransferTo", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxComboBox]
        [FlowBloxItemsSource(
            DisplayMember = nameof(FieldElement.FullyQualifiedName),
            Method = nameof(InjectionFlowBlock.GetPossibleTransferToFieldElements),
            Dependency = nameof(InjectionFlowBlock.TargetFlowBlock))]
        public FieldElement TransferTo { get; set; }

        public override string ToString() => $"{TransferFrom} → {TransferTo}";
    }

    [FlowBloxUIGroup("InjectionFlowBlock_Groups_FieldTransfers", 0)]
    [FlowBloxSpecialExplanation("InjectionFlowBlock_SpecialExplanation_TargetPathAndTransfer", Icon = SpecialExplanationIcon.Warning)]
    [Display(Name = "InjectionFlowBlock_DisplayName", Description = "InjectionFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class InjectionFlowBlock : RecursiveTargetFlowBlockBase
    {
        public InjectionFlowBlock() => FieldTransferConfigs = new ObservableCollection<InvocationFieldTransferConfig>();
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.call_merge, 16, SKColors.MediumSlateBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.call_merge, 32, SKColors.MediumSlateBlue);

        [Display(Name = "InjectionFlowBlock_FieldTransferConfigs", ResourceType = typeof(FlowBloxTexts), GroupName = "InjectionFlowBlock_Groups_FieldTransfers", Order = 0)]
        [CustomValidation(typeof(InjectionFlowBlock), nameof(ValidateFieldTransferConfigs))]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Delete)]
        [FlowBloxDataGrid]
        public ObservableCollection<InvocationFieldTransferConfig> FieldTransferConfigs { get; set; }

        protected override void OnTargetFlowBlockChanged() => OnPropertyChanged(nameof(FieldTransferConfigs));

        public static ValidationResult ValidateFieldTransferConfigs(ObservableCollection<InvocationFieldTransferConfig> transferConfigs, ValidationContext context)
        {
            var block = context.ObjectInstance as InjectionFlowBlock;
            if (block?.TargetFlowBlock is BaseResultFlowBlock target)
            {
                var unmapped = target.Fields.Where(f => !transferConfigs.Any(t => t.TransferTo == f)).ToList();
                if (unmapped.Count > 0)
                    return new ValidationResult(string.Format(FlowBloxResourceUtil.GetLocalizedString("InjectionFlowBlock_Validation_UnmappedFields"), string.Join(", ", unmapped.Select(f => f.FullyQualifiedName))), [context.MemberName]);
            }
            return ValidationResult.Success;
        }

        public List<FieldElement> GetPossibleTransferFromFieldElements() => ReferencedFlowBlocks.OfType<BaseResultFlowBlock>().SingleOrDefault()?.Fields ?? [];
        public List<FieldElement> GetPossibleTransferToFieldElements() => (TargetFlowBlock as BaseResultFlowBlock)?.Fields ?? [];

        public override ObservableCollection<BaseFlowBlock> ReferencedFlowBlocks
        {
            get => base.ReferencedFlowBlocks;
            set { base.ReferencedFlowBlocks = value; OnPropertyChanged(nameof(FieldTransferConfigs)); }
        }

        public override bool Execute(Runtime.BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {
                if (TargetFlowBlock == null) throw new InvalidOperationException("A target flow block must be defined.");
                foreach (var config in FieldTransferConfigs)
                {
                    var value = config.TransferFrom.StringValue;
                    config.TransferTo.SetValue(runtime, value);
                    runtime.Report($"The field value \"{TextHelper.ShortenString(value, 200, true)}\" was copied to \"{config.TransferTo.FullyQualifiedName}\". Source: \"{config.TransferFrom.FullyQualifiedName}\"");
                }
                TargetFlowBlock.ExecuteNextFlowBlocks(runtime, true);
            });
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties(); properties.Add(nameof(TargetFlowBlock)); properties.Add(nameof(FieldTransferConfigs)); return properties;
        }
    }
}
