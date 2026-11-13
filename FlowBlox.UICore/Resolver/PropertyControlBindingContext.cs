using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.ViewModels.PropertyView;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace FlowBlox.UICore.Resolver
{
    public class PropertyControlBindingContext
    {
        private readonly object _target;
        private readonly Dictionary<PropertyInfo, PropertyControlViewModel> _viewModels = new();

        public PropertyControlBindingContext(object target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));

            if (_target is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += OnTargetPropertyChanged;
            }
        }

        public void Register(PropertyInfo property, PropertyControlViewModel viewModel)
        {
            if (property == null || viewModel == null)
                return;

            _viewModels[property] = viewModel;

            UpdateIsActive(property, viewModel);
            UpdateIsEnabled(property, viewModel);
        }

        private void OnTargetPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            UpdateHasChanges(e);
            UpdateDependendMember(e);
        }

        private void UpdateDependendMember(PropertyChangedEventArgs e)
        {
            foreach (var kvp in _viewModels)
            {
                var property = kvp.Key;
                var viewModel = kvp.Value;

                var activationAttr = property.GetCustomAttribute<ActivationConditionAttribute>();
                var dependsAttr = property.GetCustomAttribute<DependsOnPropertyAttribute>();
                if (activationAttr?.MemberName == e.PropertyName ||
                    dependsAttr?.GetMemberNames().Contains(e.PropertyName) == true)
                {
                    UpdateIsActive(property, viewModel);
                    UpdateIsEnabled(property, viewModel);
                    RaisePropertyChanged(viewModel);
                }
            }
        }

        private void UpdateHasChanges(PropertyChangedEventArgs e)
        {
            if (!_viewModels.Any(kvp => kvp.Key.Name == e.PropertyName))
                return; // Control not yet fully loaded

            var viewModel = _viewModels.Single(kvp => kvp.Key.Name == e.PropertyName).Value;
            viewModel.HasChanges = true;
        }

        private ThreadLocal<HashSet<string>> _threadLocalPCChain = new ThreadLocal<HashSet<string>>(() => new HashSet<string>());

        private void RaisePropertyChanged(PropertyControlViewModel viewModel)
        {
            string key = string.Join("|", 
                RuntimeHelpers.GetHashCode(viewModel.Target), 
                viewModel.PropertyName);

            var pCChain = _threadLocalPCChain.Value!;
            if (!pCChain.Contains(key))
            {
                pCChain.Add(key);
                FlowBloxComponentHelper.RaisePropertyChanged(viewModel.Target, viewModel.PropertyName);
            }
        }

        private void UpdateIsActive(PropertyInfo property, PropertyControlViewModel viewModel)
        {
            var activationAttr = property.GetCustomAttribute<ActivationConditionAttribute>();
            if (activationAttr == null)
                return;

            viewModel.IsActive = activationAttr.IsActive(_target);
        }

        private void UpdateIsEnabled(PropertyInfo property, PropertyControlViewModel viewModel)
        {
            var uiAttr = property.GetFlowBloxUIAttribute();
            viewModel.IsEnabled = !FlowBlockUIAttributeHelper.IsDynamicallyReadOnly(_target, uiAttr);
        }
    }
}
