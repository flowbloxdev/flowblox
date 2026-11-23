using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class BatchExecuteToolRequestsHandler : ToolHandlerBase
    {
        private readonly Func<ToolRequest, CancellationToken, Task<ToolResponse>> _executeTool;

        public BatchExecuteToolRequestsHandler(Func<ToolRequest, CancellationToken, Task<ToolResponse>> executeTool)
        {
            _executeTool = executeTool;
        }

        public override string Name => "BatchExecuteToolRequests";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Executes multiple tool requests.",
            new JObject
            {
                ["continueOnError"] = "bool? (default: false) - continue executing remaining requests when one request fails",
                ["requests"] = "[{toolName,arguments}]"
            });

        /// <summary>
        /// Executes batch entries with parameters inside an arguments object or directly at the entry root.
        /// An explicit arguments object takes precedence over root parameters.
        /// </summary>
        /// <example>
        /// <code>
        /// { "requests": [{ "toolName": "GetTypeKindsInfo", "arguments": { "typeFullName": "Example.Type" } }] }
        /// { "requests": [{ "toolName": "GetTypeKindsInfo", "typeFullName": "Example.Type" }] }
        /// </code>
        /// </example>
        public override async Task<ToolResponse> HandleAsync(
            JObject args,
            CancellationToken ct)
        {
            if (args["requests"] is not JArray requests || !requests.Any())
            {
                return ToolHandlerUtilities.Fail("requests is required and must contain at least one entry.");
            }

            var continueOnError = args.Value<bool?>("continueOnError") ?? false;
            var allOk = true;
            var isLayoutRelevant = false;
            var batchResults = new JArray();
            var completed = new List<(JObject Item, ToolResponse Response)>();

            foreach (var request in requests.OfType<JObject>())
            {
                var toolName = request.Value<string>("toolName") ?? string.Empty;
                var argumentToken = request["arguments"];
                var invalidArguments = argumentToken != null && argumentToken is not JObject;

                if (invalidArguments
                    || string.IsNullOrWhiteSpace(toolName)
                    || string.Equals(toolName, Name, StringComparison.OrdinalIgnoreCase))
                {
                    allOk = false;
                    batchResults.Add(new JObject
                    {
                        ["toolName"] = toolName,
                        ["ok"] = false,
                        ["error"] = invalidArguments
                            ? "arguments must be a JSON object. Omit arguments to pass parameters directly on the batch request."
                            : string.IsNullOrWhiteSpace(toolName)
                            ? "toolName is required."
                            : "Nested batch not supported."
                    });

                    if (!continueOnError)
                    {
                        break;
                    }

                    continue;
                }

                var toolArgs = argumentToken as JObject;
                if (toolArgs == null)
                {
                    toolArgs = (JObject)request.DeepClone();
                    toolArgs.Remove("toolName");
                }

                var response = await _executeTool(
                    new ToolRequest
                    {
                        ToolName = toolName,
                        Arguments = toolArgs
                    },
                    ct)
                    .ConfigureAwait(false);

                allOk &= response.Ok;
                isLayoutRelevant |= response.Ok && response.IsLayoutRelevantForAutoAdjustment;
                var item = new JObject
                {
                    ["toolName"] = toolName,
                    ["ok"] = response.Ok,
                    ["error"] = response.Error,
                    ["result"] = response.Result
                };
                batchResults.Add(item);
                completed.Add((item, response));

                if (!response.Ok && !continueOnError)
                {
                    break;
                }
            }

            var payload = new JObject
            {
                ["allOk"] = allOk,
                ["continueOnError"] = continueOnError,
                ["batchResults"] = batchResults
            };

            var result = allOk ? ToolHandlerUtilities.Ok(payload)
                : ToolHandlerUtilities.Fail("One or more batch requests failed.", payload);
            result.IsLayoutRelevantForAutoAdjustment = isLayoutRelevant;
            result.RefreshBeforeSerialization = () =>
            {
                foreach (var entry in completed)
                {
                    entry.Response.RefreshForSerialization();
                    entry.Item["result"] = entry.Response.Result;
                }
            };
            result.RefreshForSerialization();
            return result;
        }
    }
}
