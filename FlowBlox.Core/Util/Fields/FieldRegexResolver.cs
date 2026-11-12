using FlowBlox.Core.Provider;
using FlowBlox.Core.Utilities;
using FlowBlox.Core.Models.Components;
using System.Text;
using System.Text.RegularExpressions;

namespace FlowBlox.Core.Util.Fields
{
    public sealed class FieldRegexResolver
    {
        private readonly object _sync = new object();
        private Regex _compiled;
        private string _lastHash;

        /// <summary>
        /// Returns a compiled regular expression that matches only the currently existing
        /// Fully Qualified Field Names in the registry. It is only rebuilt if the hash changes.
        /// </summary>
        public Regex ResolveFieldRegex()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var fields = registry?.GetFieldElements() ?? Enumerable.Empty<FieldElement>();

            var fullyQualifiedFieldNames = fields
                .Select(f => f?.FullyQualifiedName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            // Determine the hash (stable across the sorted list)
            var joined = string.Join("\n", fullyQualifiedFieldNames);
            var hash = HashHelper.ComputeSHA256Hash(Encoding.UTF8.GetBytes(joined));

            lock (_sync)
            {
                // If the hash is the same, reuse the old regex.
                if (_compiled != null && string.Equals(hash, _lastHash, StringComparison.Ordinal))
                    return _compiled;

                _lastHash = hash;

                // If no fields are available: Regex that matches nothing
                if (fullyQualifiedFieldNames.Count == 0)
                {
                    _compiled = new Regex("$^", RegexOptions.Compiled);
                    return _compiled;
                }

                // Building the regular expression
                var sb = new StringBuilder();
                sb.Append("(?:");
                for (int i = 0; i < fullyQualifiedFieldNames.Count; i++)
                {
                    if (i > 0) 
                        sb.Append("|");
                    
                    sb.Append(RegexUtil.EscapeRegexValue(fullyQualifiedFieldNames[i]));
                }
                sb.Append(")");

                _compiled = new Regex(sb.ToString(), RegexOptions.Compiled);
                return _compiled;
            }
        }

        public void Invalidate()
        {
            lock (_sync)
            {
                _compiled = null;
                _lastHash = null;
            }
        }
    }
}
