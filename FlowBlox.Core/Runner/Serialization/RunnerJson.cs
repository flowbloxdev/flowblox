using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowBlox.Core.Runner.Serialization
{
    public static class RunnerJson
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static T ReadFile<T>(string path)
        {
            var json = File.ReadAllText(path);
            var value = JsonSerializer.Deserialize<T>(json, Options);
            if (value is Contracts.RunnerRequest request)
                return (T)(object)RunnerRequestProtection.FromStorage(request);
            return value;
        }

        public static void WriteProtectedRequestFile(string path, Contracts.RunnerRequest request)
        {
            WriteFile(path, RunnerRequestProtection.ForStorage(request));
        }

        public static void WriteFile<T>(string path, T obj)
        {
            var json = JsonSerializer.Serialize(obj, Options);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, json);
        }

        public static string Serialize<T>(T obj, bool pretty = true)
        {
            var opts = new JsonSerializerOptions(Options) { WriteIndented = pretty };
            return JsonSerializer.Serialize(obj, opts);
        }

        public static string WriteFileResolved<T>(string pathTemplate, T obj, RunnerPathTemplateContext ctx = null)
        {
            var json = JsonSerializer.Serialize(obj, Options);

            ctx ??= new RunnerPathTemplateContext();
            if (string.IsNullOrWhiteSpace(ctx.ContentForHash))
                ctx = new RunnerPathTemplateContext
                {
                    ProjectName = ctx.ProjectName,
                    UtcNow = ctx.UtcNow,
                    ContentForHash = json
                };

            var resolvedPath = RunnerPathTemplateResolver.Resolve(pathTemplate, ctx);

            var dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(resolvedPath, json);
            return resolvedPath;
        }
    }
}
