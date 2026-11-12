using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider;
using Newtonsoft.Json;
using FlowBlox.Core.Attributes;

namespace FlowBlox.Core.Models.Base
{
    public partial class FlowBloxReactiveObject
    {
        private Dictionary<string, string> _propertySelectedFieldMap = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores selected fields by property name, independently of legacy companion properties.
        /// Use SetPropertySelectedFieldName to update selections and notify property editors.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        [FlowBloxImmutable]
        public Dictionary<string, string> PropertySelectedFieldMap
        {
            get => _propertySelectedFieldMap;
            set
            {
                _propertySelectedFieldMap = value == null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(value, StringComparer.Ordinal);
                OnPropertyChanged(nameof(PropertySelectedFieldMap));
            }
        }

        public bool ShouldSerializePropertySelectedFieldMap() => _propertySelectedFieldMap.Count > 0;

        /// <summary>
        /// Returns the stored field name even if the field cannot currently be resolved.
        /// </summary>
        public string GetPropertySelectedFieldName(string propertyName)
        {
            return _propertySelectedFieldMap.TryGetValue(propertyName, out var fieldName)
                ? fieldName
                : null;
        }

        /// <summary>
        /// Resolves the selection in the current registry, including transaction-local field values.
        /// </summary>
        public FieldElement GetPropertySelectedField(string propertyName)
        {
            var fieldName = GetPropertySelectedFieldName(propertyName);
            return string.IsNullOrWhiteSpace(fieldName)
                ? null
                : FlowBloxRegistryProvider.GetRegistry()?.GetFieldElementOrNull(fieldName);
        }

        /// <summary>
        /// Replaces or removes a selection and notifies both bindings and property change tracking.
        /// </summary>
        public void SetPropertySelectedFieldName(string propertyName, string fieldName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
            fieldName = string.IsNullOrWhiteSpace(fieldName) ? null : fieldName;
            if (string.Equals(GetPropertySelectedFieldName(propertyName), fieldName, StringComparison.Ordinal))
                return;

            if (fieldName == null)
                _propertySelectedFieldMap.Remove(propertyName);
            else
                _propertySelectedFieldMap[propertyName] = fieldName;

            OnPropertyChanged(nameof(PropertySelectedFieldMap));
            OnPropertyChanged(propertyName);
        }

        /// <summary>
        /// Adapts field selections from editors and legacy project properties to the serialized map.
        /// </summary>
        public void SetPropertySelectedField(string propertyName, FieldElement field)
        {
            SetPropertySelectedFieldName(propertyName, field?.FullyQualifiedName);
        }
    }
}
