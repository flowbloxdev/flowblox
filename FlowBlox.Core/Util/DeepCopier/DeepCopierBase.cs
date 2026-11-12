using FlowBlox.Core.Extensions;
using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;

namespace FlowBlox.Core.Util.DeepCopier
{
    public enum DeepCopyActions
    {
        NoCopy,
        CopyReference
    }

    public class DeepCopyAction
    {
        public Type DeclaringType { get; set; }

        public Type PropertyType { get; set; }

        public string Name { get; set; }

        public DeepCopyActions Action { get; set; }

        public Func<object, bool> ExceptCondition { get; set; }
    }

    public enum DeepCopierSpecialMode
    {
        None,
        Recopy
    }

    public delegate void OnAfterCopyEventHandler(object source, object copy);

    public abstract class DeepCopierBase<T> where T : class
    {
        protected readonly IDictionary<T, T> _copies = new Dictionary<T, T>();
        protected readonly IDictionary<T, T> _recopies = new Dictionary<T, T>();

        public event OnAfterCopyEventHandler OnAfterCopy;

        public DeepCopierSpecialMode SpecialMode { get; set; }

        public List<DeepCopyAction> PropertyActions { get; set; }

        private string _actionProtocol;
        private void AppendProtocolAction(string message)
        {
            _actionProtocol += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}" + Environment.NewLine;
        }

        public string GetProtocol() => _actionProtocol;

        public DeepCopierBase(List<DeepCopyAction> propertyActions = null)
        {
            this.PropertyActions = propertyActions;
        }

        public T Copy(T source, T copy)
        {
            if (SpecialMode == DeepCopierSpecialMode.None)
                _copies.TryAdd(source, copy);

            if (SpecialMode == DeepCopierSpecialMode.Recopy)
                _recopies.TryAdd(source, copy);

            var properties = ReflectionHelper.GetProperties(source.GetType(), BindingFlags.Instance | BindingFlags.Public);
            foreach (var property in properties)
            {
                if ((property.PropertyType.IsValueType || property.PropertyType == typeof(string)) &&
                    property.CanRead && IsCopyableProperty(property))
                {
                    var destinationProperty = copy.GetType().GetProperty(property.Name);
                    if (destinationProperty != null && destinationProperty.CanWrite)
                        destinationProperty.SetValue(copy, property.GetValue(source));
                }
            }

            foreach (var property in properties)
            {
                if (IsDictionary(property.PropertyType) && IsCopyableProperty(property))
                {
                    CopyToDictionary(source, property, copy);
                }
                else if (IsCopyableEnumerable(property))
                {
                    this.CopyToManyAssociation(source, property, copy);
                }
                else if (IsCopyable(property))
                {
                    this.CopyToOneAssociation(source, property, copy);
                }
            }
            OnAfterCopy?.Invoke(source, copy);
            return copy;
        }

        /// <summary>
        /// Clears cached recopy results while preserving the original copy mappings and current mode.
        /// </summary>
        public void ClearRecopyCache()
        {
            _recopies.Clear();
        }

        public T ResolveCopy(T source) {
            return source != null && _copies.TryGetValue(source, out var copy)
                ? copy
                : source;
        }

        public T Copy(T source)
        {
            if (source == null)
                return default;

            T copy;
            if (SpecialMode == DeepCopierSpecialMode.None && _copies.TryGetValue(source, out copy))
            {
                AppendProtocolAction($"Bereits erstellte Kopie verwendet für Objekt {source.GetHashCode()} ({source})");
                return copy;
            }

            if (SpecialMode == DeepCopierSpecialMode.Recopy && _recopies.TryGetValue(source, out copy))
            {
                AppendProtocolAction($"Bereits erstellte Kopie (Re-Copy) verwendet für Objekt {source.GetHashCode()} ({source})");
                return copy;
            }

            copy = CreateCopy(source);
            if (copy == null)
                return default;

            return Copy(source, copy);
        }

        private T CreateCopy(T source)
        {
            var copyType = GetCopyType(source.GetType());
            T copy;

            if (SpecialMode == DeepCopierSpecialMode.Recopy)
            {
                var _reversedCopies = _copies.ReverseDictionary();
                if (_reversedCopies.ContainsKey(source))
                {
                    copy = _reversedCopies[source];
                    AppendProtocolAction($"Recopy-Modus: Ursprüngliche Instanz {_reversedCopies[source].GetHashCode()} für Objekt {source.GetHashCode()} ({source}) verwendet.");
                }
                else
                {
                    copy = source;
                    AppendProtocolAction($"Recopy-Modus: Source-Instanz für Objekt {source.GetHashCode()} ({source}) verwendet.");
                }
            }
            else
            {
                copy = (T)Activator.CreateInstance(copyType);
                _copies[source] = copy;
                AppendProtocolAction($"Neue Kopie {copy.GetHashCode()} erstellt für Objekt {source.GetHashCode()} ({source}) vom Typ {source.GetType()}");
            }
            return copy;
        }

        protected abstract Type GetCopyType(Type sourceType);

        protected virtual bool IsCopyableProperty(PropertyInfo property)
        {
            if (property.GetCustomAttribute(typeof(DeepCopierIgnoreAttribute)) != null)
                return false;

            if (this.PropertyActions != null &&
                this.PropertyActions.Any(x => 
                x.DeclaringType != null && 
                x.DeclaringType.IsAssignableFrom(property.DeclaringType) && 
                x.Name == property.Name && 
                x.Action == DeepCopyActions.NoCopy))
            {
                AppendProtocolAction($"Eigenschaft '{property.Name}' von '{property.DeclaringType.Name}' ist nicht kopierbar.");
                return false;
            }

            if (this.PropertyActions != null &&
                this.PropertyActions.Any(x =>
                x.PropertyType != null &&
                x.PropertyType.IsAssignableFrom(property.PropertyType) &&
                x.Action == DeepCopyActions.NoCopy))
            {
                AppendProtocolAction($"Eigenschaft '{property.Name}' von '{property.DeclaringType.Name}' ist nicht kopierbar.");
                return false;
            }

            return true;
        }

        private static bool IsDictionary(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        }

        protected bool IsCopyableEnumerable(PropertyInfo property)
        {
            if (!IsCopyableProperty(property))
                return false;

            if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) &&
                property.PropertyType.GenericTypeArguments.Any())
            {
                var type = property.PropertyType.GenericTypeArguments.First();
                return IsCopyable(type);
            }
            return false;
        }

        protected bool IsCopyable(PropertyInfo property)
        {
            if (!IsCopyableProperty(property))
                return false;

            return IsCopyable(property.PropertyType);
        }

        protected bool IsCopyable(Type propertyType)
        {
            return !propertyType.IsValueType &&
                   propertyType != typeof(string) &&
                   typeof(T).IsAssignableFrom(propertyType);
        }

        private bool IsCopiedByReference(T source, PropertyInfo property)
        {
            var instance = property.GetValue(source, null);
            return IsCopiedByReference(source, property, instance);
        }

        private bool IsCopiedByReference(T source, PropertyInfo property, object instance)
        {
            if (this.PropertyActions == null)
                return false;

            foreach (var action in PropertyActions)
            {
                bool isDeclaringTypeMatch = action.DeclaringType?.IsAssignableFrom(source.GetType()) ?? false;
                bool isNameMatch = action.Name == property.Name;
                bool isPropertyTypeMatch = action.PropertyType?.IsAssignableFrom(property.PropertyType) ?? false;

                bool isPropertyTypeEnumerableGenericMatch = false;
                if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && property.PropertyType.IsGenericType)
                {
                    Type genericTypeArgument = property.PropertyType.GetGenericArguments()[0];
                    isPropertyTypeEnumerableGenericMatch = action.PropertyType?.IsAssignableFrom(genericTypeArgument) ?? false;
                }

                bool exceptConditionMet = false;
                if (isDeclaringTypeMatch && isNameMatch)
                {
                    exceptConditionMet = action.ExceptCondition?.Invoke(instance) ?? false;
                    if (!exceptConditionMet && action.Action == DeepCopyActions.CopyReference)
                        return true;
                }

                if (isPropertyTypeMatch || isPropertyTypeEnumerableGenericMatch)
                {
                    exceptConditionMet = action.ExceptCondition?.Invoke(instance) ?? false;
                    if (!exceptConditionMet && action.Action == DeepCopyActions.CopyReference)
                        return true;
                }
            }

            return false;
        }


        private static void CopyToDictionary(T source, PropertyInfo property, T copy)
        {
            if (!property.CanWrite)
                return;

            var dictionary = property.GetValue(source);
            if (dictionary == null)
            {
                property.SetValue(copy, null);
                return;
            }

            // Clone the container and preserve its comparer; keys and values retain their references.
            // Consider reference resolution analogous to list entries before introducing deep-copy semantics.
            var comparer = property.PropertyType
                .GetProperty(nameof(Dictionary<object, object>.Comparer))
                .GetValue(dictionary);
            var dictionaryCopy = Activator.CreateInstance(property.PropertyType, dictionary, comparer);
            property.SetValue(copy, dictionaryCopy);
        }

        private void CopyToOneAssociation(T source, PropertyInfo property, T copy)
        {
            if (!property.CanWrite)
                return;

            var instance = (T)property.GetValue(source, null);

            if (IsCopiedByReference(source, property))
            {
                AppendProtocolAction($"Referenz {instance?.GetHashCode()} verwenden für Member {property.Name} vom Typ {property.PropertyType}");
                property.SetValue(copy, instance);
            }
            else
            {
                property.SetValue(copy, Copy(instance));
            }
        }

        private void CopyToManyAssociation(T source, PropertyInfo property, T copy)
        {
            if (!property.CanWrite)
                return;

            var instances = (IEnumerable)property.GetValue(source, null);
            if (instances == null)
            {
                property.SetValue(copy, null);
                return;
            }

            var list = CreateList(property);
            foreach (var instance in instances)
            {
                if (IsCopiedByReference(source, property, instance))
                {
                    AppendProtocolAction($"Referenz {instance?.GetHashCode()} verwenden für Listen-Member {property.Name} vom Typ {property.PropertyType}");
                    list.Add(instance);
                }
                else
                {
                    var instanceCopy = Copy((T)instance);
                    if (instanceCopy != null)
                        list.Add(instanceCopy);
                }
            }
            property.SetValue(copy, list);
        }

        private static IList CreateList(PropertyInfo property)
        {
            Type itemType = property.PropertyType.GenericTypeArguments.First();

            if (property.PropertyType.GetGenericTypeDefinition() == typeof(ObservableCollection<>))
            {
                if (property.Name == "RequiredFields")
                {

                }

                Type collectionType = typeof(ObservableCollection<>).MakeGenericType(itemType);
                return (IList)Activator.CreateInstance(collectionType);
            }
            else
            {
                Type listType = typeof(List<>).MakeGenericType(itemType);
                return (IList)Activator.CreateInstance(listType);
            }
        }
    }
}
