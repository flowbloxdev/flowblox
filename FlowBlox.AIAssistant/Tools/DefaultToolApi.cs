using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.Fields;

namespace FlowBlox.AIAssistant.Tools
{
    public class DefaultToolApi : IFlowBloxAIToolApi
    {
        private const string ExecuteInputFileCommandToolName = "ExecuteInputFileCommand";
        private const string RunProjectDebugTestToolName = "RunProjectDebugTest";
        private const string ExecuteExternalProjectToolName = "ExecuteExternalProject";
        private readonly Dictionary<string, IToolHandler> _handlers;
        private readonly ToolNameResolver _toolNameResolver;
        private readonly List<ToolDefinition> _definitions;
        public Func<ToolRequest, bool>? ToolExecutionConfirmationCallback { get; set; }
        public AssistantComponentContext? ComponentContext { get; set; }

        public DefaultToolApi(Func<AssistantConfiguration>? assistantConfigurationProvider = null)
        {
            ToolHandlerUtilities.SetAssistantConfigurationProvider(assistantConfigurationProvider);
            var handlers = CreateHandlers();

            _handlers = handlers.ToDictionary(
                handler => handler.Name,
                handler => handler,
                StringComparer.OrdinalIgnoreCase);

            _toolNameResolver = new ToolNameResolver(_handlers.Keys);

            _definitions = handlers
                .Select(CreateDefinition)
                .ToList();
        }

        private static ToolDefinition CreateDefinition(IToolHandler handler)
        {
            var definition = handler.Definition;
            var purpose = Attribute.GetCustomAttribute(handler.GetType(), typeof(ToolPurposeAttribute)) as ToolPurposeAttribute;
            definition.ShortDescription = purpose?.Description ?? definition.Description;
            definition.IncludeContractInBootstrap = purpose?.IncludeContractInBootstrap == true;
            return definition;
        }

        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            var scope = ComponentContext == null ? AssistantScope.Project : AssistantScope.Component;
            return _definitions
                .Where(definition => _handlers[definition.Name].Scope == AssistantScope.All ||
                    _handlers[definition.Name].Scope == scope)
                .ToList();
        }

        public Task<ToolResponse> ExecuteAsync(ToolRequest request, CancellationToken ct)
        {
            return ExecuteAsync(request, ct, allowBatch: true);
        }

        private Task<ToolResponse> ExecuteBatchRequestAsync(ToolRequest request, CancellationToken ct)
        {
            return ExecuteAsync(request, ct, allowBatch: false);
        }

        private async Task<ToolResponse> ExecuteAsync(ToolRequest request, CancellationToken ct, bool allowBatch)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.ToolName))
                return ToolHandlerUtilities.Fail("ToolName is required.");

            IToolHandler handler;
            try
            {
                var resolvedName = _toolNameResolver.Resolve(request.ToolName);
                if (resolvedName == null)
                    return ToolHandlerUtilities.Fail($"Tool '{request.ToolName}' is not registered.");

                handler = _handlers[resolvedName];
                var scope = ComponentContext == null ? AssistantScope.Project : AssistantScope.Component;
                if (handler.Scope != AssistantScope.All && handler.Scope != scope)
                    return ToolHandlerUtilities.Fail($"Tool '{resolvedName}' is unavailable in {scope.ToString().ToLowerInvariant()} mode. Explain this limitation to the user.");
                if (!allowBatch && handler is BatchExecuteToolRequestsHandler)
                    return ToolHandlerUtilities.Fail("Nested batch not supported.");

                var definition = _definitions.Single(item => item.Name == resolvedName);
                if (!definition.IncludeContractInBootstrap &&
                    ToolHandlerUtilities.IsToolDefinitionMissing(GetToolDefinitionsHandler.GetRetentionKey(resolvedName)))
                {
                    return ToolHandlerUtilities.Fail(
                        $"Load the full contract for '{resolvedName}' with GetToolDefinitions(toolNames=[\"{resolvedName}\"]) " +
                        "and read its response before constructing this call. The tool was not executed.");
                }

                if (request.Arguments?["arguments"] is JObject
                    && handler.Definition.ArgumentsSchema.Property("arguments", StringComparison.OrdinalIgnoreCase) == null)
                {
                    return ToolHandlerUtilities.Fail(
                        $"Invalid argument structure for tool '{resolvedName}': an extra 'arguments' object was found inside the tool arguments. " +
                        "This tool does not define a parameter named 'arguments'. " +
                        "Remove the extra wrapper and pass its parameters directly in toolCalls[i].arguments. " +
                        "For example, use {\"arguments\":{\"fieldChangeId\":2}} instead of {\"arguments\":{\"arguments\":{\"fieldChangeId\":2}}}. " +
                        "Keep the outer 'arguments' object of the tool call. The tool was not executed. Retry with the corrected structure.");
                }

                // All execution policies must see the canonical name, including approval handling.
                request = new ToolRequest
                {
                    ToolName = resolvedName,
                    Arguments = request.Arguments,
                    CorrelationId = request.CorrelationId,
                    ConfirmationInputParameters = request.ConfirmationInputParameters,
                    RememberProjectApproval = request.RememberProjectApproval
                };

                ComponentContext?.ValidateTool(request);

                if (RequiresUserConfirmation(request))
                {
                    request = await CreateConfirmationRequestAsync(request, ct).ConfigureAwait(false);
                    if (!IsToolExecutionApproved(request))
                    {
                        return ToolHandlerUtilities.Fail(
                            $"Execution for tool '{request.ToolName}' was cancelled by user confirmation.",
                            new JObject
                            {
                                ["cancelledByUser"] = true,
                                ["toolName"] = request.ToolName
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                return ToolHandlerUtilities.Fail(ex.Message);
            }

            return await ExecuteHandlerAndNotifyChanges(handler, request, ct).ConfigureAwait(false);
        }

        private async Task<ToolResponse> ExecuteHandlerAndNotifyChanges(IToolHandler handler, ToolRequest request, CancellationToken ct)
        {
            if (ComponentContext?.DispatchAsync != null)
                return await ComponentContext.DispatchAsync(() => ExecuteHandlerInScopeAsync(handler, request, ct)).ConfigureAwait(false);

            return await ExecuteHandlerInScopeAsync(handler, request, ct).ConfigureAwait(false);
        }

        private async Task<ToolResponse> ExecuteHandlerInScopeAsync(IToolHandler handler, ToolRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ComponentContext?.ValidateTool(request);
            using var componentScope = ToolHandlerUtilities.BeginComponentScope(ComponentContext);
            var project = ToolHandlerUtilities.TryGetProject();
            using var registryScope = project == null
                ? null
                : FlowBloxRegistryProvider.BeginScopedRegistry(ComponentContext?.Registry ?? project.FlowBloxRegistry);
            var registeredBeforeCreation = ComponentContext != null &&
                (request.ToolName == "CreateField" || request.ToolName == "CreateManagedObject")
                    ? ComponentContext.CaptureRegisteredObjects()
                    : null;
            ToolResponse response;
            try
            {
                response = await handler.HandleAsync(request.Arguments ?? new JObject(), ct);
            }
            catch (Exception exception) when (ComponentContext != null && exception is not OperationCanceledException)
            {
                response = ToolHandlerUtilities.Fail(exception.Message);
            }

            if (ComponentContext != null)
            {
                if (registeredBeforeCreation != null)
                    ComponentContext.TrackCreatedObjects(registeredBeforeCreation);
                if (request.ToolName.StartsWith("Update", StringComparison.Ordinal) || registeredBeforeCreation != null)
                {
                    var refresh = response.RefreshBeforeSerialization;
                    var context = ComponentContext;
                    response.RefreshBeforeSerialization = () =>
                    {
                        refresh?.Invoke();
                        context.RefreshCreationWarning(response.Result);
                    };
                    response.RefreshForSerialization();
                }
                response.IsLayoutRelevantForAutoAdjustment = false;
                if (request.ToolName.StartsWith("Update", StringComparison.Ordinal) ||
                    request.ToolName.StartsWith("Create", StringComparison.Ordinal) ||
                    request.ToolName == "ConnectFlowBlocks" || request.ToolName == "DisconnectFlowBlocks")
                {
                    if (ComponentContext.ChangedAsync != null)
                        await ComponentContext.ChangedAsync();
                }
                return response;
            }

            response.IsLayoutRelevantForAutoAdjustment =
                response.IsLayoutRelevantForAutoAdjustment ||
                (response.Ok && handler.IsLayoutRelevantForAutoAdjustment);

            return response;
        }

        private static bool RequiresUserConfirmation(ToolRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ToolName))
                return false;

            if (string.Equals(request.ToolName, ExecuteInputFileCommandToolName, StringComparison.OrdinalIgnoreCase))
                return true;

            var isDebugTest = string.Equals(request.ToolName, RunProjectDebugTestToolName, StringComparison.OrdinalIgnoreCase);
            if (!isDebugTest && !string.Equals(request.ToolName, ExecuteExternalProjectToolName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!isDebugTest && !string.IsNullOrWhiteSpace(request.Arguments?.Value<string>("projectSpaceGuid")))
                return true;

            var projectFile = FlowBloxFieldHelper.ReplaceFieldsInString(request.Arguments?.Value<string>("projectFile") ?? string.Empty);
            if (string.IsNullOrWhiteSpace(projectFile))
            {
                if (!isDebugTest)
                    return true;
                if (!SkillProjectSessionStore.TryGetActiveProjectInfo(ToolHandlerUtilities.CurrentSessionGuid, out _, out projectFile))
                    return false;
            }

            return !SkillProjectApprovalStore.IsApproved(projectFile);
        }

        private bool IsToolExecutionApproved(ToolRequest request)
        {
            try
            {
                if (ToolExecutionConfirmationCallback?.Invoke(request) != true)
                    return false;

                if (request.RememberProjectApproval && string.IsNullOrWhiteSpace(request.Arguments?.Value<string>("projectSpaceGuid")) &&
                    (string.Equals(request.ToolName, ExecuteExternalProjectToolName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(request.ToolName, RunProjectDebugTestToolName, StringComparison.OrdinalIgnoreCase)))
                {
                    var projectFile = request.Arguments?.Value<string>("projectFile") ??
                        request.Arguments?.Value<string>("confirmationProjectFile");
                    if (!string.IsNullOrWhiteSpace(projectFile) && File.Exists(projectFile))
                        SkillProjectApprovalStore.Approve(projectFile);
                }

                return true;
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    $"Failed to obtain or persist user approval for AI Assistant tool '{request.ToolName}'.",
                    ex);
                return false;
            }
        }

        private static async Task<ToolRequest> CreateConfirmationRequestAsync(ToolRequest request, CancellationToken ct)
        {
            var isDebugTest = string.Equals(request.ToolName, RunProjectDebugTestToolName, StringComparison.OrdinalIgnoreCase);
            if (!isDebugTest && !string.Equals(request.ToolName, ExecuteExternalProjectToolName, StringComparison.OrdinalIgnoreCase))
                return request;

            var projectFile = FlowBloxFieldHelper.ReplaceFieldsInString(request.Arguments?.Value<string>("projectFile") ?? string.Empty);
            FlowBloxProject project;
            var isProjectSpace = !isDebugTest && !string.IsNullOrWhiteSpace(request.Arguments?.Value<string>("projectSpaceGuid"));
            var usesActiveProject = string.IsNullOrWhiteSpace(projectFile) && !isProjectSpace;
            if (isProjectSpace)
            {
                project = await ProjectSpaceToolClient.LoadAsync(request.Arguments, ct).ConfigureAwait(false);
                projectFile = Path.Combine(
                    Path.GetTempPath(), "FlowBloxAiAssistantReview", Guid.NewGuid().ToString("N"),
                    Core.Util.IOUtil.GetValidFileName(project.ProjectName) + ".fbprj");
                Directory.CreateDirectory(Path.GetDirectoryName(projectFile)!);
                File.WriteAllText(projectFile, Newtonsoft.Json.JsonConvert.SerializeObject(project, Core.Util.Json.JsonSettings.ProjectExport()));
            }
            else if (usesActiveProject)
            {
                if (!isDebugTest || !SkillProjectSessionStore.TryGetActiveProjectInfo(
                    ToolHandlerUtilities.CurrentSessionGuid,
                    out project,
                    out projectFile))
                    return request;
            }
            else
            {
                if (!File.Exists(projectFile))
                    return request;
                project = SkillProjectSerializer.FromFile(projectFile);
            }

            var arguments = request.Arguments == null
                ? new JObject()
                : new JObject(request.Arguments);
            if (isProjectSpace)
            {
                arguments["confirmationProjectFile"] = projectFile;
                arguments["projectSpaceGuid"] = project.ProjectSpaceGuid;
                arguments["projectSpaceVersion"] = project.ProjectSpaceVersion;
            }
            else if (usesActiveProject)
            {
                arguments.Remove("projectFile");
                arguments["confirmationProjectFile"] = projectFile;
            }
            else
                arguments["projectFile"] = projectFile;

            var inputs = ProjectExecutionToolUtilities.ResolveInputParameters(arguments["inputParameters"] as JObject, project);
            var effectiveInputs = project.UserFields
                .Where(field => field.UserFieldType == UserFieldTypes.Input)
                .ToDictionary(
                    field => field.Name,
                    field => inputs.TryGetValue(field.Name, out var value) && !string.IsNullOrEmpty(value)
                        ? value
                        : FlowBloxFieldHelper.ReplaceFieldsInString(field.DefaultValue, resolveVaultReferences: false),
                    StringComparer.OrdinalIgnoreCase);

            var inputSnapshot = JObject.FromObject(effectiveInputs);
            inputSnapshot.AddAnnotation(new ProjectExecutionToolUtilities.ResolvedInputParameters(effectiveInputs));
            arguments["inputParameters"] = inputSnapshot;

            return new ToolRequest
            {
                ToolName = request.ToolName,
                Arguments = arguments,
                CorrelationId = request.CorrelationId,
                ConfirmationInputParameters = effectiveInputs
            };
        }

        private List<IToolHandler> CreateHandlers()
        {
            return
            [
                new GetToolDefinitionsHandler(GetToolDefinitions),
                new GetProjectJsonHandler(),
                new LoadExplanationHandler(),
                new GetDistributedDataHandler(),
                new GetDistributedDataContentHandler(),
                new GetProjectMetadataHandler(),
                new GetSkillProjectsHandler(),
                new GetPSProjectsHandler(),
                new GetSkillProjectHandler(),
                new CreateOrUpdateSkillProjectHandler(),
                new SkillProjectEditingHandler(),
                new GetRootCategoriesHandler(),
                new GetCategoryChildrenHandler(),
                new SearchFlowBlockHandler(),
                new SearchOptionsHandler(),
                new SetOptionValueHandler(),
                new GetComponentSnapshotHandler(),
                new CreateFlowBlockHandler(),
                new UpdateFlowBlockHandler(),
                new DeleteFlowBlockHandler(),
                new ConnectFlowBlocksHandler(),
                new DisconnectFlowBlocksHandler(),
                new CreateManagedObjectHandler(),
                new CreateUserFieldHandler(),
                new UpdateManagedObjectHandler(),
                new DeleteManagedObjectHandler(),
                new GetManagedObjectsTypesHandler(),
                new GetSupportedTypesHandler(),
                new GetTypeKindsInfoHandler(),
                new GetManagedObjectKindsInfoHandler(),
                new ResolveAssociatedFlowBlockHandler(),
                new GetPlaceholdersHandler(),
                new GetToolboxEntriesHandler(),
                new GetInputFilesHandler(),
                new GetInputFileContentHandler(),
                new GetOutputFileContentHandler(),
                new CreateFieldHandler(),
                new CreateOrUpdateInputFileHandler(),
                new ExecuteInputFileCommandHandler(),
                new DeleteInputFileHandler(),
                new RunProjectDebugTestHandler(),
                new ExecuteExternalProjectHandler(),
                new RunTestDefinitionHandler(),
                new RunGenerationStrategiesHandler(),
                new GetLastDebugArtefactHandler(),
                new InspectFieldValueHandler(),
                new GetLatestTestExecutionArtifactHandler(),
                new InspectExecutedTestFieldValueHandler(),
                new InspectOutputFieldValueHandler(),
                new BatchExecuteToolRequestsHandler(ExecuteBatchRequestAsync)
            ];
        }
    }
}
