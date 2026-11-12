using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using Newtonsoft.Json.Serialization;

namespace FlowBlox.Core.Util.Json.ValueProvider
{
    /// <summary>
    /// Imports old companion field properties directly into the central selection map.
    /// Json.NET resolves project references before calling this provider.
    /// </summary>
    internal sealed class LegacySelectedFieldValueProvider : IValueProvider
    {
        private readonly string _propertyName;

        public LegacySelectedFieldValueProvider(string propertyName)
        {
            _propertyName = propertyName;
        }

        public object GetValue(object target) => null;

        public void SetValue(object target, object value)
        {
            ((FlowBloxReactiveObject)target).SetPropertySelectedField(_propertyName, (FieldElement)value);
        }
    }
}
