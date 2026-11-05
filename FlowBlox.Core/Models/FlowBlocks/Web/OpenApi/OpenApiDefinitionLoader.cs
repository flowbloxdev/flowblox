using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Text;

namespace FlowBlox.Core.Models.FlowBlocks.Web.OpenApi
{
    internal static class OpenApiDefinitionLoader
    {
        internal sealed record Definition(IReadOnlyList<OpenApiEndpointDescriptor> Endpoints, string Warnings);
        private sealed record CacheEntry(DateTimeOffset Created, Definition Definition);
        private static readonly ConcurrentDictionary<string, CacheEntry> UrlCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
        private static readonly HttpClient DefinitionClient = new();

        public static Definition Load(string source, bool forceRefresh = false)
        {
            if (string.IsNullOrWhiteSpace(source)) return new Definition([], string.Empty);
            var resolved = source.Trim();
            var isUrl = Uri.TryCreate(resolved, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
            if (isUrl && !forceRefresh && UrlCache.TryGetValue(resolved, out var cached) &&
                DateTimeOffset.UtcNow - cached.Created < CacheDuration)
                return cached.Definition;

            OpenApiDocument document;
            string warnings;
            string sourceUrl = isUrl ? resolved : null;
            if (isUrl)
            {
                // Some providers serve valid JSON as text/plain.
                // Select the reader from the document content instead of treating the HTTP media subtype as its format.
                var content = DefinitionClient.GetStringAsync(uri).GetAwaiter().GetResult();
                var result = OpenApiDocument.Parse(
                    content,
                    GetFormat(content),
                    new OpenApiReaderSettings { BaseUrl = uri });
                document = result.Document;
                warnings = ReadDiagnostics(result.Diagnostic?.Errors);
            }
            else if (File.Exists(resolved))
            {
                var result = OpenApiDocument.LoadAsync(resolved, new OpenApiReaderSettings(), CancellationToken.None)
                    .GetAwaiter().GetResult();
                document = result.Document;
                warnings = ReadDiagnostics(result.Diagnostic?.Errors);
            }
            else
            {
                var result = OpenApiDocument.Parse(resolved, GetFormat(resolved), new OpenApiReaderSettings());
                document = result.Document;
                warnings = ReadDiagnostics(result.Diagnostic?.Errors);
            }

            if (document == null) throw new InvalidOperationException("The OpenAPI definition could not be parsed.");
            var definition = new Definition(CreateDescriptors(document, sourceUrl), warnings);
            if (isUrl) UrlCache[resolved] = new CacheEntry(DateTimeOffset.UtcNow, definition);
            return definition;
        }

        private static string GetFormat(string content)
            => content.TrimStart().StartsWith('{') ? "json" : "yaml";

        private static string ReadDiagnostics(IEnumerable<OpenApiError> errors)
        {
            var diagnostics = errors?.ToList() ?? [];
            // Semantic validation can flag unrelated provider endpoints. Keep them visible,
            // but reserve load failures for parser/reference errors that prevent interpretation.
            var messages = diagnostics.Where(x => x is not OpenApiValidatorError)
                .Select(x => x.Message).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (messages.Length > 0) throw new FormatException("Invalid OpenAPI definition: " + string.Join("; ", messages));
            return string.Join(Environment.NewLine, diagnostics.OfType<OpenApiValidatorError>().Select(x => x.Message));
        }

        private static IReadOnlyList<OpenApiEndpointDescriptor> CreateDescriptors(OpenApiDocument document, string sourceUrl)
        {
            var result = new List<OpenApiEndpointDescriptor>();
            var documentServer = document.Servers?.FirstOrDefault();
            foreach (var pathEntry in document.Paths ?? [])
            {
                var pathItem = pathEntry.Value;
                foreach (var operationEntry in pathItem.Operations ?? [])
                {
                    var operation = operationEntry.Value;
                    var group = operation.Tags?.FirstOrDefault()?.Name ?? "Other";
                    var method = operationEntry.Key.Method.ToUpperInvariant();
                    var requestContent = operation.RequestBody?.Content;
                    var contentEntry = requestContent?
                        .OrderByDescending(x => x.Key.Contains("json", StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(x => x.Key.Contains("xml", StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault();
                    var mediaType = contentEntry?.Value;
                    var parameters = (pathItem.Parameters ?? [])
                        .Concat(operation.Parameters ?? [])
                        .Where(x => x != null)
                        .GroupBy(x => $"{x.In}:{x.Name}", StringComparer.OrdinalIgnoreCase)
                        .Select(x => ToParameter(x.Last()))
                        .ToList();
                    var payload = mediaType == null ? string.Empty : CreatePayloadExample(mediaType);
                    var summary = operation.Summary ?? operation.Description ?? operation.OperationId ?? string.Empty;
                    result.Add(new OpenApiEndpointDescriptor
                    {
                        Group = group,
                        Method = method,
                        Path = pathEntry.Key,
                        Summary = summary,
                        DisplayName = $"{group} | {method} {pathEntry.Key}" + (string.IsNullOrWhiteSpace(summary) ? string.Empty : $" - {summary}"),
                        ServerUrl = ResolveServer(operation.Servers?.FirstOrDefault() ?? pathItem.Servers?.FirstOrDefault() ?? documentServer, sourceUrl),
                        ContentType = contentEntry?.Key ?? string.Empty,
                        PayloadExample = payload,
                        Parameters = parameters
                    });
                }
            }
            return result.OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => MethodOrder(x.Method)).ToArray();
        }

        private static OpenApiParameterValue ToParameter(IOpenApiParameter parameter)
        {
            var value = parameter.Example?.ToString() ?? parameter.Schema?.Default?.ToString() ?? string.Empty;
            return new OpenApiParameterValue
            {
                Name = parameter.Name,
                Location = parameter.In?.ToString() ?? string.Empty,
                Required = parameter.Required,
                Description = parameter.Description ?? string.Empty,
                Value = TrimJsonString(value)
            };
        }

        private static string CreatePayloadExample(IOpenApiMediaType mediaType)
        {
            if (mediaType.Example != null) return FormatExample(mediaType.Example.ToString());
            if (mediaType.Examples?.FirstOrDefault().Value?.Value != null)
                return FormatExample(mediaType.Examples.First().Value.Value.ToString());
            return mediaType.Schema == null ? string.Empty : JsonConvert.SerializeObject(CreateSchemaValue(mediaType.Schema), Formatting.Indented);
        }

        private static object CreateSchemaValue(IOpenApiSchema schema, int depth = 0)
        {
            if (depth > 8)
                return null;

            if (schema.Example != null)
                return ConvertExample(schema.Example.ToString());

            if (schema.Default != null)
                return ConvertExample(schema.Default.ToString());

            var type = schema.Type?.ToString() ?? string.Empty;
            if (type.Contains("Object", StringComparison.OrdinalIgnoreCase) || schema.Properties?.Count > 0)
                return (schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
                    .ToDictionary(x => x.Key, x => CreateSchemaValue(x.Value, depth + 1));
            if (type.Contains("Array", StringComparison.OrdinalIgnoreCase))
                return schema.Items == null ? Array.Empty<object>() : new[] { CreateSchemaValue(schema.Items, depth + 1) };
            if (type.Contains("Boolean", StringComparison.OrdinalIgnoreCase))
                return false;
            if (type.Contains("Integer", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (type.Contains("Number", StringComparison.OrdinalIgnoreCase))
                return 0.0;
            return schema.Enum?.FirstOrDefault()?.ToString() is { Length: > 0 } enumValue ? TrimJsonString(enumValue) : string.Empty;
        }

        private static object ConvertExample(string value)
        {
            try { return JToken.Parse(value).ToObject<object>(); }
            catch (JsonReaderException) { return value; }
        }

        private static string FormatExample(string value)
        {
            try { return JToken.Parse(value).ToString(Formatting.Indented); }
            catch (JsonReaderException) { return value; }
        }

        private static string TrimJsonString(string value)
            => value?.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value ?? string.Empty;

        private static string ResolveServer(OpenApiServer definition, string sourceUrl)
        {
            var server = definition?.Url;
            if (definition?.Variables != null)
            {
                foreach (var variable in definition.Variables)
                    server = server?.Replace("{" + variable.Key + "}", variable.Value.Default);
            }

            if (string.IsNullOrWhiteSpace(server))
            {
                if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)) return string.Empty;
                return sourceUri.GetLeftPart(UriPartial.Authority);
            }
            if (Uri.TryCreate(server, UriKind.Absolute, out var absolute)) return absolute.ToString().TrimEnd('/');
            if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var baseUri)) return new Uri(baseUri, server).ToString().TrimEnd('/');
            return server.TrimEnd('/');
        }

        private static int MethodOrder(string method) => method switch
        {
            "GET" => 0,
            "POST" => 1,
            "PUT" => 2,
            "PATCH" => 3,
            "DELETE" => 4,
            _ => 5
        };
    }
}
