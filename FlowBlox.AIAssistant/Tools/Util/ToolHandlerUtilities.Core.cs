using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.FlowBlocks;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static partial class ToolHandlerUtilities
    {
        private static readonly AsyncLocal<string?> CurrentSessionGuidScope = new();

        private static readonly NullabilityInfoContext _nullabilityInfoContext = new();

        private static readonly HashSet<string> ExcludedFlowBloxComponentProperties = new(StringComparer.Ordinal)
        {
            nameof(FlowBloxComponent.HandleRequirements),
            nameof(FlowBloxComponent.HasErrors),
            nameof(FlowBloxComponent.Icon16),
            nameof(FlowBloxComponent.Icon32),
            nameof(FlowBloxComponent.Version)
        };

        private static readonly HashSet<string> ExcludedBaseFlowBlockProperties = new(StringComparer.Ordinal)
        {
            nameof(BaseFlowBlock.AssociatedIterationContext),    // Covered by iteration guidance.
            nameof(BaseFlowBlock.BreakPoint),                    // Not relevant to AI flow construction.
            nameof(BaseFlowBlock.DefinedManagedObjects),         // Resolved by the FlowBlock.
            nameof(BaseFlowBlock.ExecutionIndex),                // Covered by flow-order guidance.
            nameof(BaseFlowBlock.HasIterationContext),           // Derived from the flow; available in snapshots/project JSON.
            nameof(BaseFlowBlock.InheritRequirementsNotMet),     // Covered by conditions and decisions guidance.
            nameof(BaseFlowBlock.IsNotExecuted),                 // Implied by flow connections.
            nameof(BaseFlowBlock.IterationContext),              // Computed; use snapshots/project JSON and AssociatedIterationContext.
            nameof(BaseFlowBlock.Location),                      // Available in snapshots/project JSON.
            nameof(BaseFlowBlock.NextFlowBlocks),                // Derived from connections; available in snapshots/project JSON.
            nameof(BaseFlowBlock.NotificationTypes),             // Notifications are not exposed to the AI Assistant.
            nameof(BaseFlowBlock.OverriddenNotificationEntries), // Notifications are not exposed to the AI Assistant.
            nameof(BaseFlowBlock.Parent),                        // Runtime-only relationship.
            nameof(BaseFlowBlock.ReferencedFlowBlocks),          // Managed by connection tools; available in snapshots/project JSON.
            nameof(BaseFlowBlock.Size),                          // Available in snapshots/project JSON.
            nameof(BaseFlowBlock.TestDefinitions)                // Covered by dedicated guidance and tools.
        };


        public static ToolDefinition CreateDefinition(string name, string description, JObject? argumentsSchema = null)
        {
            return new ToolDefinition
            {
                Name = name,
                Description = description,
                ArgumentsSchema = argumentsSchema ?? new JObject()
            };
        }

        public static ToolResponse Ok(JObject result)
        {
            return new ToolResponse
            {
                Ok = true,
                Result = result
            };
        }

        public static ToolResponse Fail(string error, JObject? result = null)
        {
            return new ToolResponse
            {
                Ok = false,
                Error = error,
                Result = result ?? new JObject()
            };
        }

        public static void SetCurrentSessionGuid(string? sessionGuid)
        {
            CurrentSessionGuidScope.Value = string.IsNullOrWhiteSpace(sessionGuid)
                ? null
                : sessionGuid.Trim();
        }

        public static void ClearSessionCache(string? sessionGuid = null)
        {
            var key = string.IsNullOrWhiteSpace(sessionGuid)
                ? CurrentSessionGuidScope.Value
                : sessionGuid.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                ClearSessionCache("default-session");
                return;
            }

            AssistantSessionContentStore.Clear(key);
            SkillProjectSessionStore.Clear(key);
            AiAssistantProjectRunState.Clear(key);
            AiAssistantTestExecutionState.Clear(key);
        }

        public static AssistantSessionContentCache CurrentContentCache => AssistantSessionContentStore.Get(CurrentSessionGuid);

        public static string CurrentSessionGuid => GetCurrentSessionCacheKey();

        private static string GetCurrentSessionCacheKey() => CurrentSessionGuidScope.Value ?? "default-session";

        public static FlowBloxRegistry GetRegistry()
        {
            return GetProject().FlowBloxRegistry;
        }

        public static FlowBloxProject GetProject()
        {
            return SkillProjectSessionStore.GetCurrentProject(GetCurrentSessionCacheKey());
        }

        public static FlowBloxProject? TryGetProject() =>
            SkillProjectSessionStore.TryGetCurrentProject(GetCurrentSessionCacheKey(), out var project)
                ? project
                : null;

        public static bool IsSkillProjectActive() =>
            SkillProjectSessionStore.IsActive(GetCurrentSessionCacheKey());

        public static string? GetActiveSkillProjectName() =>
            SkillProjectSessionStore.TryGetActiveProject(GetCurrentSessionCacheKey(), out var project)
                ? project.ProjectName
                : null;

        public static string[] PathOf(FlowBlockCategory category)
        {
            var stack = new Stack<string>();
            var current = category;
            while (current != null)
            {
                stack.Push(current.DisplayName);
                current = current.ParentCategory;
            }

            return stack.ToArray();
        }

        public static Type? ResolveType(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            var requested = fullName.Trim();

            var direct = Type.GetType(requested, throwOnError: false);
            if (direct != null)
                return direct;

            var commaIndex = requested.IndexOf(',');
            var typeNameOnly = commaIndex > 0
                ? requested[..commaIndex].Trim()
                : requested;

            var resolved = ReflectionHelper.GetTypeByClass(typeNameOnly);
            if (resolved != null)
                return resolved;

            resolved = ReflectionHelper.GetTypeByFullNameFromLastPart(typeNameOnly);
            if (resolved != null)
                return resolved;

            return ReflectionHelper.GetTypeByClass(requested);
        }

        public static JObject ToTypeInfo(FlowBloxReactiveObject instance)
        {
            return new JObject
            {
                ["fullName"] = instance.GetType().FullName ?? instance.GetType().Name,
                ["displayName"] = FlowBloxComponentHelper.GetDisplayName(instance),
                ["description"] = FlowBloxComponentHelper.GetDescription(instance)
            };
        }

        public static IManagedObject? ResolveManagedObjectByName(FlowBloxRegistry registry, string? managedObjectName)
        {
            if (string.IsNullOrWhiteSpace(managedObjectName))
            {
                return null;
            }

            return registry.GetManagedObjects()
                .FirstOrDefault(x => string.Equals(x.Name, managedObjectName, StringComparison.OrdinalIgnoreCase));
        }

        public static FieldElement? ResolveFieldElementByFQName(FlowBloxRegistry registry, string? fullyQualifiedFieldName)
        {
            if (string.IsNullOrWhiteSpace(fullyQualifiedFieldName))
            {
                return null;
            }

            return registry.GetFieldElementOrNull(fullyQualifiedFieldName);
        }

        public static BaseFlowBlock? ResolveFlowBlockByName(FlowBloxRegistry registry, string? flowBlockName)
        {
            if (string.IsNullOrWhiteSpace(flowBlockName))
            {
                return null;
            }

            return registry.GetFlowBlocks()
                .FirstOrDefault(x => string.Equals(x.Name, flowBlockName, StringComparison.OrdinalIgnoreCase));
        }

        public static Type? GetElementType(Type type)
        {
            if (type == typeof(string))
            {
                return null;
            }

            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType
                && type.GetGenericArguments().Length == 1
                && typeof(IEnumerable).IsAssignableFrom(type))
            {
                return type.GetGenericArguments()[0];
            }

            var enumerableInterface = type
                .GetInterfaces()
                .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableInterface?.GetGenericArguments()[0];
        }

        private sealed class PathSegment
        {
            public bool IsProperty { get; set; }
            public string? PropertyName { get; set; }
            public int Index { get; set; }
            public string? ExplicitTypeFullName { get; set; }
        }

        private sealed class UpdateOperation
        {
            public string Path { get; set; } = string.Empty;
            public JToken Value { get; set; } = JValue.CreateNull();
        }

    }
}
