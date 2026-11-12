using System.Reflection;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Fields;

namespace FlowBlox.Core.Models.Runtime
{
    /// <summary>
    /// Applies scalar field values for one invocation, including owned reactive configuration.
    /// Always restores configured literals, including when resolution or execution fails.
    /// Strings retain their existing inline placeholder evaluation semantics.
    /// </summary>
    internal sealed class SelectedFieldPropertyScope : IDisposable
    {
        private readonly List<(FlowBloxReactiveObject Owner, PropertyInfo Property, object Value)> _originals = new();

        public SelectedFieldPropertyScope(IEnumerable<FlowBloxReactiveObject> owners)
        {
            try
            {
                foreach (var owner in owners)
                {
                    foreach (var selection in owner.PropertySelectedFieldMap)
                    {
                        var property = owner.GetType().GetProperty(selection.Key);
                        if (property == null || !FlowBloxFieldSelection.Supports(property)
                            || !FlowBloxFieldSelection.SupportsScalar(property.PropertyType))
                            continue;

                        var field = owner.GetPropertySelectedField(selection.Key);
                        if (field == null)
                            continue;

                        var value = FlowBloxFieldHelper.ResolveSimpleFieldValue(property.PropertyType, field.Value);
                        _originals.Add((owner, property, property.GetValue(owner)));
                        property.SetValue(owner, value);
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            for (var index = _originals.Count - 1; index >= 0; index--)
            {
                var original = _originals[index];
                original.Property.SetValue(original.Owner, original.Value);
            }

            _originals.Clear();
        }
    }
}
