using FlowBlox.Enums;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Deletion;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Testing
{
    [Display(Name = "FlowBloxTestDefinition_DisplayName", Description = "FlowBloxTestDefinition_Description", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("FlowBloxTestDefinition_DisplayName_Plural", typeof(FlowBloxTexts))]
    public class FlowBloxTestDefinition : ManagedObject, IDeletionMeasureProvider
    {
        private ObservableCollection<FlowBlockTestDataset> _entries;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.clipboard_check_outline, 16, SKColors.SeaGreen);

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.clipboard_check_outline, 32, SKColors.SeaGreen);

        [Display(Name = "FlowBloxTestDefinition_Entries", Description = "FlowBloxTestDefinition_Entries_Description", ResourceType = typeof(FlowBloxTexts))]
        public ObservableCollection<FlowBlockTestDataset> Entries
        {
            get => _entries;
            set
            {
                if (!ReferenceEquals(_entries, value))
                {
                    if (_entries != null)
                        _entries.CollectionChanged -= TestDatasets_CollectionChanged;

                    _entries = value ?? new ObservableCollection<FlowBlockTestDataset>();
                    _entries.CollectionChanged += TestDatasets_CollectionChanged;

                    OnPropertyChanged();
                    RecalculateRequiredFlagsAcrossDefinition();
                }
            }
        }

        private void TestDatasets_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var dataset in e.NewItems.OfType<FlowBlockTestDataset>())
                    dataset.ParentTestDefinition = this;
            }

            if (e.OldItems != null)
            {
                foreach (var dataset in e.OldItems.OfType<FlowBlockTestDataset>())
                {
                    if (ReferenceEquals(dataset.ParentTestDefinition, this))
                        dataset.ParentTestDefinition = null;
                }
            }

            RecalculateRequiredFlagsAcrossDefinition();
        }

        /// <summary>
        /// Aggregates all required fields and required associated-flow-block datasets for active entries.
        /// Sets/removes UI-required flags on datasets/configurations.
        /// </summary>
        public void RecalculateRequiredFlagsAcrossDefinition()
        {
            if (!IsLoaded)
                return;

            var executedDatasets = Entries
                .Where(ds => ds.Execute && ds.FlowBlock != null)
                .ToList();

            var requiredAssociatedFlowBlocks = executedDatasets
                .SelectMany(ds => ResolveAssociatedFlowBlocks(ds.FlowBlock))
                .ToHashSet();

            var requiredFQNames = executedDatasets
                .SelectMany(ds => ds.FlowBlock.GetRequiredFields())
                .Select(f => f.FullyQualifiedName)
                .Where(fqn => !string.IsNullOrWhiteSpace(fqn))
                .ToHashSet();

            foreach (var dataset in Entries)
            {
                dataset.ParentTestDefinition = this;
                dataset.UIAssociatedFlowBlockRequiredForExecution = dataset.FlowBlock != null
                    && requiredAssociatedFlowBlocks.Contains(dataset.FlowBlock);
            }

            foreach (var cfg in Entries.SelectMany(ds => ds.FlowBloxTestConfigurations))
            {
                var fQFieldName = cfg.FieldElement?.FullyQualifiedName;
                var isRequired = !string.IsNullOrWhiteSpace(fQFieldName)
                    && requiredFQNames.Contains(fQFieldName);
                cfg.UIRequiredForExecution = isRequired;
            }
        }

        internal static IEnumerable<BaseFlowBlock> ResolveAssociatedFlowBlocks(BaseFlowBlock flowBlock)
        {
            foreach (var property in AssociatedFlowBlockResolver.GetResolvableProperties(flowBlock))
            {
                var resolution = AssociatedFlowBlockResolver.Resolve(flowBlock, property);
                if (resolution.FlowBlock != null)
                    yield return resolution.FlowBlock;
            }
        }

        public override void OnAfterLoad()
        {
            base.OnAfterLoad();

            foreach (var dataset in Entries)
                dataset.ParentTestDefinition = this;

            RecalculateRequiredFlagsAcrossDefinition();
        }

        [Display(Name = "FlowBloxTestDefinition_RequiredForExecution", Description = "FlowBloxTestDefinition_RequiredForExecution_Description", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool RequiredForExecution { get; set; }

        public FlowBloxTestDefinition()
        {
            Entries = new ObservableCollection<FlowBlockTestDataset>();
        }

        public override List<string> GetDisplayableProperties()
            => [nameof(Name)];

        public void ContributeDeletionMeasures(IList<DeletionDependency> dependencies)
        {
            foreach (var dataset in Entries.ToList())
            {
                var datasetObjects = dataset.GetNestedReactiveObjects()
                    .Cast<object>()
                    .Prepend(dataset)
                    .ToHashSet(ReferenceEqualityComparer.Instance);
                var removesDataset = dependencies.Any(x =>
                    ReferenceEquals(x.ReferencingObject, dataset) &&
                    x.Property.Name == nameof(FlowBlockTestDataset.FlowBlock) &&
                    x.Impact == DeletionImpact.DirectReferenceRemoved);

                if (removesDataset)
                {
                    RemoveDependenciesFor(dependencies, datasetObjects);
                    dependencies.Add(new DeletionDependency
                    {
                        ReferencingObject = this,
                        Property = typeof(FlowBloxTestDefinition).GetProperty(nameof(Entries))!,
                        ReferencedObject = dataset,
                        Impact = DeletionImpact.CollectionEntryRemoved,
                        ReviewStatus = DeletionReviewStatus.Information,
                        EditTarget = this,
                        CollectionIndex = Entries.IndexOf(dataset)
                    });
                    continue;
                }

                foreach (var configuration in dataset.FlowBloxTestConfigurations.ToList())
                {
                    if (!dependencies.Any(x =>
                            ReferenceEquals(x.ReferencingObject, configuration) &&
                            x.Property.Name == nameof(FlowBloxFieldTestConfiguration.FieldElement) &&
                            x.Impact == DeletionImpact.DirectReferenceRemoved))
                    {
                        continue;
                    }

                    var configurationObjects = configuration.GetNestedReactiveObjects()
                        .Cast<object>()
                        .Prepend(configuration)
                        .ToHashSet(ReferenceEqualityComparer.Instance);
                    RemoveDependenciesFor(dependencies, configurationObjects);
                    dependencies.Add(new DeletionDependency
                    {
                        ReferencingObject = dataset,
                        Property = typeof(FlowBlockTestDataset).GetProperty(nameof(FlowBlockTestDataset.FlowBloxTestConfigurations))!,
                        ReferencedObject = configuration,
                        Impact = DeletionImpact.CollectionEntryRemoved,
                        ReviewStatus = DeletionReviewStatus.Information,
                        EditTarget = this,
                        CollectionIndex = dataset.FlowBloxTestConfigurations.IndexOf(configuration)
                    });
                }
            }
        }

        private static void RemoveDependenciesFor(
            IList<DeletionDependency> dependencies,
            IReadOnlySet<object> owners)
        {
            for (var i = dependencies.Count - 1; i >= 0; i--)
            {
                if (owners.Contains(dependencies[i].ReferencingObject))
                    dependencies.RemoveAt(i);
            }
        }
    }
}
