using FlowBlox.Core.Util.DistributedData;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static class AuxiliaryProjectCatalog
    {
        public static bool IsSupportedMode(string mode) =>
            string.Equals(mode, "session", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "distributedData", StringComparison.OrdinalIgnoreCase);

        public static IEnumerable<JObject> GetProjects(string mode) =>
            string.Equals(mode, "distributedData", StringComparison.OrdinalIgnoreCase)
                ? GetDistributedProjects()
                : GetSessionProjects();

        private static IEnumerable<JObject> GetSessionProjects()
        {
            var sessionId = ToolHandlerUtilities.CurrentSessionGuid;
            var projects = AuxiliaryProjectSessionStore
                .GetProjects(sessionId)
                .Select(x => ToSessionJson(AuxiliaryProjectMetadataReader.Read(x.ProjectFile), isOpen: false))
                .ToList();

            if (AuxiliaryProjectSessionStore.TryGetActiveProjectInfo(
                    sessionId,
                    out var activeProject,
                    out var activeProjectFile))
            {
                projects.RemoveAll(x => string.Equals(
                    x.Value<string>("projectFile"),
                    activeProjectFile,
                    StringComparison.OrdinalIgnoreCase));
                projects.Add(ToSessionJson(
                    AuxiliaryProjectMetadataReader.Read(activeProject, activeProjectFile),
                    isOpen: true));
            }

            return projects;
        }

        private static IEnumerable<JObject> GetDistributedProjects()
        {
            var projects = DistributedDataCatalog.GetDataTypes()
                .Single(x => string.Equals(
                    x.Name,
                    DistributedDataCatalog.AuxiliaryProjectType,
                    StringComparison.OrdinalIgnoreCase))
                .FileNames
                .Select(fileName => new
                {
                    FileName = fileName,
                    ProjectFile = DistributedDataCatalog.ResolvePath(
                        DistributedDataCatalog.AuxiliaryProjectType,
                        fileName)
                })
                .Where(x => x.ProjectFile != null)
                .Select(x => ToDistributedDataJson(
                    AuxiliaryProjectMetadataReader.Read(x.ProjectFile!),
                    x.FileName));

            return projects;
        }

        private static JObject ToSessionJson(AuxiliaryProjectMetadata metadata, bool isOpen)
        {
            var result = ToJson(metadata);
            result["projectFile"] = metadata.ProjectFile;
            result["isOpen"] = isOpen;
            return result;
        }

        private static JObject ToDistributedDataJson(AuxiliaryProjectMetadata metadata, string fileName)
        {
            var result = ToJson(metadata);
            result["fileName"] = fileName;
            result["projectFile"] = metadata.ProjectFile;
            return result;
        }

        private static JObject ToJson(AuxiliaryProjectMetadata metadata)
        {
            var result = new JObject
            {
                ["projectName"] = metadata.ProjectName,
                ["shortDescription"] = metadata.ShortDescription,
                ["description"] = metadata.Description,
                ["inputFields"] = new JArray(metadata.InputFields),
                ["outputFields"] = new JArray(metadata.OutputFields)
            };

            if (metadata.InputFieldListOfValues.Count > 0)
                result["inputFieldListOfValues"] = JObject.FromObject(metadata.InputFieldListOfValues);

            return result;
        }
    }
}
