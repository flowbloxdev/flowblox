using FlowBlox.Core.Models.Project;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Net;

namespace FlowBloxTest.Models.Project
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class FlowBloxProjectSaveTests
    {
        [TestMethod]
        public void Save_PreservesAssistantMetadataInNestedStructure()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxTest", Guid.NewGuid().ToString("N"));
            var file = Path.Combine(directory, "project.fbprj");
            Directory.CreateDirectory(directory);
            try
            {
                var project = new FlowBloxProject
                {
                    ProjectName = "Project with summary",
                    AssistantMetadata = new AssistantMetadata
                    {
                        ShortDescription = "Compact capability summary."
                    }
                };

                project.Save(file);

                var json = JObject.Parse(File.ReadAllText(file));
                var metadata = json[nameof(FlowBloxProject.AssistantMetadata)]!;
                Assert.AreEqual(project.AssistantMetadata.ShortDescription, metadata.Value<string>(nameof(AssistantMetadata.ShortDescription)));
                Assert.IsNull(json["ShortDescription"]);
                var reloaded = FlowBloxProject.FromFile(file);
                Assert.AreEqual(project.AssistantMetadata.ShortDescription, reloaded.AssistantMetadata.ShortDescription);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void Save_WithNewProjectGuid_UpdatesProjectAndSerializedGuid()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxTest", Guid.NewGuid().ToString("N"));
            var file = Path.Combine(directory, "copy.fbprj");
            Directory.CreateDirectory(directory);
            try
            {
                var project = new FlowBloxProject { ProjectName = "Copy" };
                var originalGuid = project.ProjectGuid;
                ProjectGuidChangedEventArgs eventArgs = null;
                project.ProjectGuidChanged += (_, e) => eventArgs = e;

                project.Save(file, generateNewProjectGuid: true);

                var serializedGuid = Guid.Parse(
                    JObject.Parse(File.ReadAllText(file)).Value<string>(nameof(FlowBloxProject.ProjectGuid))!);
                Assert.AreNotEqual(originalGuid, project.ProjectGuid);
                Assert.AreEqual(project.ProjectGuid, serializedGuid);
                Assert.IsNotNull(eventArgs);
                Assert.AreEqual(originalGuid, eventArgs.OldProjectGuid);
                Assert.AreEqual(project.ProjectGuid, eventArgs.NewProjectGuid);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void Save_WithoutNewProjectGuid_PreservesProjectGuid()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxTest", Guid.NewGuid().ToString("N"));
            var file = Path.Combine(directory, "project.fbprj");
            Directory.CreateDirectory(directory);
            try
            {
                var project = new FlowBloxProject { ProjectName = "Original" };
                var originalGuid = project.ProjectGuid;

                project.Save(file);

                Assert.AreEqual(originalGuid, project.ProjectGuid);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void Save_ProjectSpaceProjectLocally_PreservesBothProjectIdentities()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxTest", Guid.NewGuid().ToString("N"));
            var file = Path.Combine(directory, "materialized.fbprj");
            Directory.CreateDirectory(directory);
            try
            {
                var project = new FlowBloxProject
                {
                    ProjectName = "Project Space project",
                    ProjectSpaceGuid = "project-space-guid"
                };
                var originalGuid = project.ProjectGuid;

                project.Save(file);

                Assert.AreEqual(originalGuid, project.ProjectGuid);
                Assert.AreEqual("project-space-guid", project.ProjectSpaceGuid);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void SaveAs_MaterializedProjectSpaceProject_ChangesOnlyInternalProjectGuid()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxTest", Guid.NewGuid().ToString("N"));
            var originalFile = Path.Combine(directory, "materialized.fbprj");
            var copyFile = Path.Combine(directory, "copy.fbprj");
            Directory.CreateDirectory(directory);
            try
            {
                var project = new FlowBloxProject
                {
                    ProjectName = "Project Space project",
                    ProjectSpaceGuid = "project-space-guid"
                };
                project.Save(originalFile);
                var originalGuid = project.ProjectGuid;

                project.Save(copyFile, generateNewProjectGuid: true);

                Assert.AreNotEqual(originalGuid, project.ProjectGuid);
                Assert.AreEqual("project-space-guid", project.ProjectSpaceGuid);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void Save_WithNewProjectGuid_RestoresOriginalGuidWhenSaveFails()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxTest", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var project = new FlowBloxProject { ProjectName = "Failed copy" };
                var originalGuid = project.ProjectGuid;
                var eventCount = 0;
                project.ProjectGuidChanged += (_, _) => eventCount++;

                Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                    project.Save(directory, generateNewProjectGuid: true));

                Assert.AreEqual(originalGuid, project.ProjectGuid);
                Assert.AreEqual(0, eventCount);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public async Task SaveToProjectSpace_WithNewProjectGuid_UpdatesProjectAndSerializedGuid()
        {
            var project = new FlowBloxProject { ProjectName = "Copy" };
            var originalGuid = project.ProjectGuid;
            var webApi = new StubWebApiService(HttpStatusCode.OK);
            ProjectGuidChangedEventArgs eventArgs = null;
            project.ProjectGuidChanged += (_, e) => eventArgs = e;

            var response = await project.SaveToProjectSpaceAsync(
                "project-space-guid",
                "token",
                webApi,
                generateNewProjectGuid: true);

            Assert.IsTrue(response.Success);
            Assert.AreNotEqual(originalGuid, project.ProjectGuid);
            Assert.AreEqual(project.ProjectGuid, GetSerializedProjectGuid(webApi.LastRequestContent));
            Assert.IsNotNull(eventArgs);
            Assert.AreEqual(originalGuid, eventArgs.OldProjectGuid);
            Assert.AreEqual(project.ProjectGuid, eventArgs.NewProjectGuid);
        }

        [TestMethod]
        public async Task SaveToProjectSpace_WithoutNewProjectGuid_PreservesProjectGuid()
        {
            var project = new FlowBloxProject { ProjectName = "Original" };
            var originalGuid = project.ProjectGuid;
            var webApi = new StubWebApiService(HttpStatusCode.OK);

            var response = await project.SaveToProjectSpaceAsync(
                "project-space-guid",
                "token",
                webApi);

            Assert.IsTrue(response.Success);
            Assert.AreEqual(originalGuid, project.ProjectGuid);
        }

        [TestMethod]
        public async Task SaveToProjectSpace_WithNewProjectGuid_RestoresOriginalGuidWhenUploadFails()
        {
            var project = new FlowBloxProject { ProjectName = "Failed copy" };
            var originalGuid = project.ProjectGuid;
            var webApi = new StubWebApiService(HttpStatusCode.BadRequest);
            var eventCount = 0;
            project.ProjectGuidChanged += (_, _) => eventCount++;

            var response = await project.SaveToProjectSpaceAsync(
                "project-space-guid",
                "token",
                webApi,
                generateNewProjectGuid: true);

            Assert.IsFalse(response.Success);
            Assert.AreEqual(originalGuid, project.ProjectGuid);
            Assert.AreEqual(0, eventCount);
        }

        private static Guid GetSerializedProjectGuid(string requestContent)
        {
            var contentBase64 = JObject.Parse(requestContent).Value<string>("ContentBase64");
            using var zipStream = new MemoryStream(Convert.FromBase64String(contentBase64!));
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            using var reader = new StreamReader(archive.GetEntry("project_file.json")!.Open());
            var projectJson = JObject.Parse(reader.ReadToEnd());
            return Guid.Parse(projectJson.Value<string>(nameof(FlowBloxProject.ProjectGuid))!);
        }

        private sealed class StubWebApiService : FlowBloxWebApiService
        {
            private readonly StubHttpMessageHandler _handler;

            public StubWebApiService(HttpStatusCode statusCode)
                : base("https://flowblox.test/")
            {
                _handler = new StubHttpMessageHandler(statusCode);
                _httpClient = new HttpClient(_handler);
            }

            public string LastRequestContent => _handler.LastRequestContent;
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;

            public StubHttpMessageHandler(HttpStatusCode statusCode)
            {
                _statusCode = statusCode;
            }

            public string LastRequestContent { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                LastRequestContent = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent("{\"error\":\"Test failure\"}")
                };
            }
        }
    }
}
