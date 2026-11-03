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
        private readonly List<ToolDefinition> _definitions;
        public Func<ToolRequest, bool>? ToolExecutionConfirmationCallback { get; set; }

        public DefaultToolApi(Func<AssistantConfiguration>? assistantConfigurationProvider = null)
        {
            ToolHandlerUtilities.SetAssistantConfigurationProvider(assistantConfigurationProvider);
            var handlers = CreateHandlers();

            _handlers = handlers.ToDictionary(
                handler => handler.Name,
                handler => handler,
                StringComparer.OrdinalIgnoreCase);

            _definitions = handlers
                .Select(handler => handler.Definition)
                .ToList();
        }

        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            return _definitions;
        }

        public Task<ToolResponse> ExecuteAsync(ToolRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.ToolName))
                return Task.FromResult(ToolHandlerUtilities.Fail("ToolName is required."));

            if (!_handlers.TryGetValue(request.ToolName, out var handler))
                return Task.FromResult(ToolHandlerUtilities.Fail($"Tool '{request.ToolName}' is not registered."));

            try
            {
                if (RequiresUserConfirmation(request))
                {
                    request = CreateConfirmationRequest(request);
                    if (!IsToolExecutionApproved(request))
                    {
                        return Task.FromResult(ToolHandlerUtilities.Fail(
                            $"Execution for tool '{request.ToolName}' was cancelled by user confirmation.",
                            new JObject
                            {
                                ["cancelledByUser"] = true,
                                ["toolName"] = request.ToolName
                            }));
                    }
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }

            return ExecuteHandlerAndNotifyChanges(handler, request, ct);
        }

        private async Task<ToolResponse> ExecuteHandlerAndNotifyChanges(IToolHandler handler, ToolRequest request, CancellationToken ct)
        {
            var project = ToolHandlerUtilities.TryGetProject();
            using var registryScope = project == null
                ? null
                : FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var response = await handler
                .HandleAsync(request.Arguments ?? new JObject(), ct)
                .ConfigureAwait(false);

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

            var projectFile = FlowBloxFieldHelper.ReplaceFieldsInString(request.Arguments?.Value<string>("projectFile") ?? string.Empty);
            if (string.IsNullOrWhiteSpace(projectFile))
            {
                if (!isDebugTest)
                    return true;
                if (!AuxiliaryProjectSessionStore.TryGetActiveProjectInfo(ToolHandlerUtilities.CurrentSessionGuid, out _, out projectFile))
                    return false;
            }

            return !AuxiliaryProjectApprovalStore.IsApproved(projectFile);
        }

        private bool IsToolExecutionApproved(ToolRequest request)
        {
            try
            {
                if (ToolExecutionConfirmationCallback?.Invoke(request) != true)
                    return false;

                if (request.RememberProjectApproval &&
                    (string.Equals(request.ToolName, ExecuteExternalProjectToolName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(request.ToolName, RunProjectDebugTestToolName, StringComparison.OrdinalIgnoreCase)))
                {
                    var projectFile = request.Arguments?.Value<string>("projectFile") ??
                        request.Arguments?.Value<string>("confirmationProjectFile");
                    if (!string.IsNullOrWhiteSpace(projectFile) && File.Exists(projectFile))
                        AuxiliaryProjectApprovalStore.Approve(projectFile);
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

        private static ToolRequest CreateConfirmationRequest(ToolRequest request)
        {
            var isDebugTest = string.Equals(request.ToolName, RunProjectDebugTestToolName, StringComparison.OrdinalIgnoreCase);
            if (!isDebugTest && !string.Equals(request.ToolName, ExecuteExternalProjectToolName, StringComparison.OrdinalIgnoreCase))
                return request;

            var projectFile = FlowBloxFieldHelper.ReplaceFieldsInString(request.Arguments?.Value<string>("projectFile") ?? string.Empty);
            FlowBloxProject project;
            var usesActiveProject = string.IsNullOrWhiteSpace(projectFile);
            if (usesActiveProject)
            {
                if (!isDebugTest || !AuxiliaryProjectSessionStore.TryGetActiveProjectInfo(
                    ToolHandlerUtilities.CurrentSessionGuid,
                    out project,
                    out projectFile))
                    return request;
            }
            else
            {
                if (!File.Exists(projectFile))
                    return request;
                project = AuxiliaryProjectSerializer.FromFile(projectFile);
            }

            var arguments = request.Arguments == null
                ? new JObject()
                : new JObject(request.Arguments);
            if (usesActiveProject)
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
                    field => inputs.TryGetValue(field.Name, out var value)
                        ? value
                        : FlowBloxFieldHelper.ReplaceFieldsInString(field.StringValue ?? string.Empty),
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
                new GetProjectJsonHandler(),
                new LoadExplanationHandler(),
                new GetDistributedDataHandler(),
                new GetDistributedDataContentHandler(),
                new GetProjectMetadataHandler(),
                new GetAuxiliaryProjectsHandler(),
                new GetAuxiliaryProjectHandler(),
                new CreateOrUpdateAuxiliaryProjectHandler(),
                new AuxiliaryProjectEditingHandler(),
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
                new BatchExecuteToolRequestsHandler(ExecuteAsync)
            ];
        }
    }
}
