using System.Diagnostics;
using System.Text.Json;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static class ProjectExecutionToolUtilities
    {
        public static Dictionary<string, string> ResolveInputParameters(
            JObject? inputParameters,
            FlowBloxProject project)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (inputParameters == null)
                return result;

            var snapshot = inputParameters.Annotation<ResolvedInputParameters>();
            if (snapshot != null)
                return new Dictionary<string, string>(snapshot.Values, StringComparer.OrdinalIgnoreCase);

            var availableFields = project.UserFields
                .Where(x => x.UserFieldType == UserFieldTypes.Input)
                .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in inputParameters.Properties())
            {
                if (!availableFields.ContainsKey(parameter.Name))
                {
                    var available = string.Join(", ", availableFields.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                    throw new InvalidOperationException(
                        $"Input parameter '{parameter.Name}' does not exist in project '{project.ProjectName}'. Available input parameters: {available}.");
                }
                if (parameter.Value.Type is JTokenType.Object or JTokenType.Array)
                    throw new InvalidOperationException($"Input parameter '{parameter.Name}' must be a textual or scalar value.");

                result[parameter.Name] = FlowBloxFieldHelper.ReplaceFieldsInString(
                    parameter.Value.Type == JTokenType.Null 
                        ? string.Empty 
                        : parameter.Value.ToString(), resolveVaultReferences: false);
            }

            return result;
        }

        internal sealed record ResolvedInputParameters(Dictionary<string, string> Values);

        public static void WriteJson(string path, object value) =>
            File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

        public static int StartRunnerHost(
            RunnerHostResolver.RunnerHostCommand command,
            string requestFile,
            string responseFile,
            CancellationToken ct = default)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.BuildArguments(requestFile, responseFile),
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("RunnerHost process could not be started.");
            using var cancellationRegistration = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        "Failed to terminate RunnerHost after cancellation of an AI Assistant project execution.",
                        ex);
                }
            });

            process.WaitForExit();
            ct.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
    }
}
