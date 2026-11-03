using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using FlowBlox.Core.Services;

namespace FlowBlox.AvaloniaDesktop.Services
{
    public class AssemblyPreloader : IAssemblyPreloader
    {
        private readonly HashSet<string> _visitedAssemblies = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] _importantAssemblies = [
            "FlowBlox.Core",
            "FlowBlox.UICore"
        ];

        public void PreloadReferencedAssemblies()
        {
            Preload(Assembly.GetExecutingAssembly(), _importantAssemblies);
        }

        private void Preload(Assembly rootAssembly, params string[] importantAssemblies)
        {
            Trace.WriteLine($"[FlowBlox] Preloading assemblies for: {rootAssembly.GetName().Name}");

            foreach (var reference in rootAssembly.GetReferencedAssemblies())
            {
                if (_visitedAssemblies.Contains(reference.FullName))
                    continue;

                var loaded = Assembly.Load(reference);
                _visitedAssemblies.Add(reference.FullName);

                Trace.WriteLine($"[FlowBlox] Loaded: {loaded.FullName}");

                if (importantAssemblies.Contains(reference.Name, StringComparer.OrdinalIgnoreCase))
                    Preload(loaded, importantAssemblies);
            }

            Trace.WriteLine($"[FlowBlox] Done preloading for {rootAssembly.GetName().Name}");
        }
    }
}
