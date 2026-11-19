using System.IO.Compression;
using System.Net;
using System.Text;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using Newtonsoft.Json;

namespace FlowBloxTest.Models.Project;

internal sealed class ProjectSpaceCacheHttpHandler : HttpMessageHandler
{
    internal string ProjectSpaceGuid { get; } = Guid.NewGuid().ToString("N");
    internal DateTime? CreatedAt { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    internal DateTime? UpdatedAt { get; set; }
    internal DateTime VersionCreatedAt { get; } = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    internal string ProjectName { get; set; } = "Cached project";
    internal HttpStatusCode MetadataStatus { get; set; } = HttpStatusCode.OK;
    internal int MetadataCalls { get; private set; }
    internal int ContentCalls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        object body;
        var status = HttpStatusCode.OK;
        if (request.RequestUri!.AbsolutePath.EndsWith("/project.php"))
        {
            MetadataCalls++;
            status = MetadataStatus;
            body = new FbProjectResult
            {
                Guid = ProjectSpaceGuid,
                Name = ProjectName,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                Versions = [new FbProjectVersionResult { VersionNumber = 1, CreatedAt = VersionCreatedAt, UpdatedAt = UpdatedAt }]
            };
        }
        else
        {
            ContentCalls++;
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(archive.CreateEntry("project_file.json").Open()))
            {
                writer.Write(JsonConvert.SerializeObject(new
                {
                    ProjectGuid = Guid.NewGuid(),
                    ProjectName,
                    UserFields = Array.Empty<object>(),
                    FlowBlocks = Array.Empty<object>(),
                    ManagedObjects = Array.Empty<object>()
                }));
            }
            body = new { Content = Convert.ToBase64String(memory.ToArray()) };
        }

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
        });
    }
}
