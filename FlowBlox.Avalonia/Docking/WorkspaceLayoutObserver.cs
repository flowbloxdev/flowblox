using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Dock.Model.Core;

namespace FlowBlox.AvaloniaDesktop.Docking;

/// <summary>Observes the serialized Dock model, including hidden and floating panels.</summary>
internal sealed class WorkspaceLayoutObserver : IDisposable
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> DataProperties = new();
    private readonly IDockable _root;
    private readonly Action _changed;
    private readonly HashSet<INotifyPropertyChanged> _models = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<INotifyCollectionChanged> _collections = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public WorkspaceLayoutObserver(IDockable root, Action changed)
    {
        _root = root;
        _changed = changed;
        RefreshSubscriptions();
    }

    private static PropertyInfo[] GetDataProperties(Type type) => DataProperties.GetOrAdd(type,
        modelType => modelType.GetProperties().Where(property => property.CanRead &&
            property.GetIndexParameters().Length == 0 &&
            property.IsDefined(typeof(DataMemberAttribute), true)).ToArray());

    private void RefreshSubscriptions()
    {
        Unsubscribe();
        Visit(_root);
    }

    private void Visit(object value)
    {
        if (value is IDockable or IDockWindow)
        {
            if (value is not INotifyPropertyChanged model || !_models.Add(model)) return;
            model.PropertyChanged += ModelChanged;
            foreach (var property in GetDataProperties(value.GetType())) Visit(property.GetValue(value));
        }
        else if (value is IEnumerable items && value is not string)
        {
            if (value is INotifyCollectionChanged collection && _collections.Add(collection))
                collection.CollectionChanged += CollectionChanged;
            if (value is IDictionary dictionary)
            {
                foreach (var child in dictionary.Values) Visit(child);
            }
            else
            {
                foreach (var child in items) Visit(child);
            }
        }
    }

    private void ModelChanged(object sender, PropertyChangedEventArgs e)
    {
        // Ignore runtime-only properties such as the native control content and pointer state.
        if (_disposed || (!string.IsNullOrEmpty(e.PropertyName) &&
            !GetDataProperties(sender.GetType()).Any(property => property.Name == e.PropertyName))) return;
        RefreshSubscriptions();
        _changed();
    }

    private void CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed) return;
        RefreshSubscriptions();
        _changed();
    }

    private void Unsubscribe()
    {
        foreach (var model in _models) model.PropertyChanged -= ModelChanged;
        foreach (var collection in _collections) collection.CollectionChanged -= CollectionChanged;
        _models.Clear();
        _collections.Clear();
    }

    public void Dispose()
    {
        _disposed = true;
        Unsubscribe();
    }
}
