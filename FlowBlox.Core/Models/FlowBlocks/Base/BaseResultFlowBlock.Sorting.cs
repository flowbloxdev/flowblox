using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Enums;

namespace FlowBlox.Core.Models.FlowBlocks.Base;

public abstract partial class BaseResultFlowBlock
{
    private ObservableCollection<OutputSortRule> _outputSorting = new();
    private bool _synchronizingOutputSorting;
    private readonly HashSet<INotifyCollectionChanged> _outputFieldCollections = new();
    private readonly HashSet<FlowBloxReactiveObject> _outputFieldDefinitions = new();

    [Display(Name = "BaseResultFlowBlock_OutputSorting", Description = "BaseResultFlowBlock_OutputSorting_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseResultFlowBlock_Groups_Output", Order = 3)]
    [FlowBloxAssociation(Operations = UIOperations.Edit)]
    [FlowBloxDataGrid(IsMovable = true, ColumnMemberNames = new[] { nameof(OutputSortRule.Field), nameof(OutputSortRule.Direction) })]
    public ObservableCollection<OutputSortRule> OutputSorting
    {
        get
        {
            SynchronizeOutputSorting();
            return _outputSorting;
        }
        set
        {
            _outputSorting = value ?? new();
            OnPropertyChanged();
        }
    }

    public override void OnAfterLoad()
    {
        base.OnAfterLoad();
        SynchronizeOutputSorting();
    }

    protected override void OnPropertyChanged(string propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (IsLoaded && !_synchronizingOutputSorting)
            SynchronizeOutputSorting();
    }

    private void OutputFieldsChanged(object sender, NotifyCollectionChangedEventArgs args) => SynchronizeOutputSorting();
    private void OutputFieldChanged(object sender, PropertyChangedEventArgs args) => SynchronizeOutputSorting();

    private void SynchronizeOutputSorting()
    {
        if (_synchronizingOutputSorting || !IsLoaded)
            return;

        _synchronizingOutputSorting = true;
        try
        {
            var fields = Fields.Where(field => field != null).Distinct().ToList();
            var seen = new HashSet<FieldElement>();
            foreach (var rule in _outputSorting.ToList())
            {
                if (rule?.Field == null || !fields.Contains(rule.Field) || !seen.Add(rule.Field))
                    _outputSorting.Remove(rule);
            }
            foreach (var field in fields.Where(field => !seen.Contains(field)))
                _outputSorting.Add(new OutputSortRule { Field = field });

            // Observe field collections and their declaration rows, including replacement of a result field in a row.
            foreach (var collection in _outputFieldCollections)
                collection.CollectionChanged -= OutputFieldsChanged;
            foreach (var definition in _outputFieldDefinitions)
                definition.PropertyChanged -= OutputFieldChanged;
            _outputFieldCollections.Clear();
            _outputFieldDefinitions.Clear();

            foreach (var property in GetType().GetProperties().Where(property =>
                property.Name != nameof(OutputSorting) &&
                typeof(INotifyCollectionChanged).IsAssignableFrom(property.PropertyType)))
            {
                if (property.GetValue(this) is not INotifyCollectionChanged collection)
                    continue;
                if (_outputFieldCollections.Add(collection))
                    collection.CollectionChanged += OutputFieldsChanged;
                if (collection is not IEnumerable entries)
                    continue;
                foreach (var definition in entries.OfType<FlowBloxReactiveObject>().Where(entry => entry is not FlowBloxComponent))
                {
                    if (_outputFieldDefinitions.Add(definition))
                        definition.PropertyChanged += OutputFieldChanged;
                }
            }
        }
        finally
        {
            _synchronizingOutputSorting = false;
        }
    }

    public override DeletionReviewStatus GetDeletionReviewStatus(
        object owner, PropertyInfo property, DeletionImpact impact, PropertyInfo componentProperty)
    {
        if (componentProperty?.Name == nameof(OutputSorting) && owner is OutputSortRule &&
            property.Name == nameof(OutputSortRule.Field) && impact == DeletionImpact.DirectReferenceRemoved)
            return DeletionReviewStatus.Information;

        return base.GetDeletionReviewStatus(owner, property, impact, componentProperty);
    }

    private IEnumerable<Dictionary<FieldElement, string>> SortOutputDatasets(IEnumerable<Dictionary<FieldElement, string>> rows)
    {
        if (rows == null)
            return rows;

        IOrderedEnumerable<Dictionary<FieldElement, string>> sorted = null;
        foreach (var rule in OutputSorting.Where(rule => rule.Direction != OutputSortDirection.None))
        {
            Func<Dictionary<FieldElement, string>, object> key = row =>
            {
                if (!row.TryGetValue(rule.Field, out var rawValue))
                    return null;

                var fieldType = rule.Field.FieldType;
                if (fieldType == null)
                    return rawValue;

                return fieldType.ParseDatasetValue(rawValue);
            };

            var descending = rule.Direction == OutputSortDirection.Descending;
            if (sorted == null)
            {
                sorted = descending
                    ? rows.OrderByDescending(key, OutputSortValueComparer.Instance)
                    : rows.OrderBy(key, OutputSortValueComparer.Instance);
            }
            else
            {
                sorted = descending
                    ? sorted.ThenByDescending(key, OutputSortValueComparer.Instance)
                    : sorted.ThenBy(key, OutputSortValueComparer.Instance);
            }
        }

        return sorted?.ToList() ?? rows;
    }
}
