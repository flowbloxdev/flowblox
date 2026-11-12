using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using FlowBlox.Core.Util.FlowBlocks;
using SkiaSharp;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;

namespace FlowBlox.Core.Models.Base
{
    /// <summary>
    /// Base class that provides support for property change notifications (INotifyPropertyChanged)
    /// and validation error notifications (INotifyDataErrorInfo).
    /// Designed for use in data-bound components that support validation via DataAnnotations.
    /// </summary>
    public partial class FlowBloxReactiveObject : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        [ThreadStatic]
        private static HashSet<FlowBloxReactiveObject> _afterLoadVisitedObjects;

        /// <summary>
        /// Indicates whether initialization has completed and user changes may be tracked.
        /// </summary>
        protected bool IsLoaded { get; private set; }

        public IEnumerable<FlowBloxReactiveObject> GetNestedReactiveObjects() => FlowBlockObjectCollector.CollectNestedReactiveObjectsRecursive(this);

        public virtual void OnAfterLoad()
        {
            var ownsTraversal = _afterLoadVisitedObjects == null;
            _afterLoadVisitedObjects ??= new HashSet<FlowBloxReactiveObject>(ReferenceEqualityComparer.Instance);
            try
            {
                if (!_afterLoadVisitedObjects.Add(this))
                    return;

                IsLoaded = true;
                foreach (var reactiveObject in GetNestedReactiveObjects())
                {
                    if (!_afterLoadVisitedObjects.Contains(reactiveObject))
                        reactiveObject.OnAfterLoad();
                }
            }
            finally
            {
                if (ownsTraversal)
                    _afterLoadVisitedObjects = null;
            }
        }

        [JsonIgnore]
        public virtual SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cube_outline, 16, new SKColor(84, 110, 122));

        [JsonIgnore]
        public virtual SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cube_outline, 32, new SKColor(84, 110, 122));

        private readonly Dictionary<string, List<string>> _errors = new();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

        public bool HasErrors => _errors.Any();

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                // Alle Fehler zurückgeben
                return _errors.Values.SelectMany(e => e).ToList();
            }

            if (_errors.TryGetValue(propertyName, out var messages))
                return messages;

            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Validates all properties using DataAnnotations and returns the errors as a dictionary.
        /// Raises notifications via INotifyDataErrorInfo.
        /// </summary>
        public Dictionary<string, List<string>> ValidateObject()
        {
            // Remember previously validated properties (those with errors)
            var previousErrorProperties = _errors.Keys.ToList();

            _errors.Clear();

            var results = new List<ValidationResult>();
            var context = new ValidationContext(this);

            Validator.TryValidateObject(this, context, results, validateAllProperties: true);

            // Apply current validation results
            foreach (var result in results)
            {
                foreach (var memberName in result.MemberNames.Distinct())
                {
                    if (!_errors.ContainsKey(memberName))
                        _errors[memberName] = new List<string>();

                    _errors[memberName].Add(result.ErrorMessage);

                    // Notify about invalid property
                    ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(memberName));
                }
            }

            // Notify that the following properties are now valid
            foreach (var clearedProperty in previousErrorProperties.Except(_errors.Keys))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(clearedProperty));
            }

            return _errors;
        }
    }
}
