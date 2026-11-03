using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [DoNotParallelize]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class AuxiliaryProjectHandlerTests
    {
        private readonly string _sessionId = Guid.NewGuid().ToString("N");
        private readonly string _projectName = "Schema inspection " + Guid.NewGuid().ToString("N");
        private const string ProjectDescription = "Inspects a database schema for the current assistant session.";
        private string _previousLocalAppDataDirectory = null!;
        private string _localAppDataDirectory = null!;
        private string _previousApprovals = null!;

        [TestInitialize]
        public void Initialize()
        {
            var option = FlowBloxOptions.GetOptionInstance().GetOption("Paths.LocalAppDataDir");
            _previousLocalAppDataDirectory = option.Value;
            _localAppDataDirectory = Path.Combine(Path.GetTempPath(), "FlowBloxAuxiliaryTests", Guid.NewGuid().ToString("N"));
            option.Value = _localAppDataDirectory;
            var approvals = FlowBloxOptions.GetOptionInstance().GetOption(AuxiliaryProjectApprovalStore.OptionName);
            _previousApprovals = approvals.PersistentValue;
            approvals.PersistentValue = string.Empty;
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject { ProjectName = "Main" };
            ToolHandlerUtilities.SetCurrentSessionGuid(_sessionId);
        }

        [TestCleanup]
        public void Cleanup()
        {
            FlowBloxOptions.GetOptionInstance().GetOption(AuxiliaryProjectApprovalStore.OptionName).PersistentValue = _previousApprovals;
            ToolHandlerUtilities.ClearSessionCache(_sessionId);
            ToolHandlerUtilities.SetCurrentSessionGuid(null);
            foreach (var projectName in new[]
                     {
                         _projectName,
                         _projectName + " renamed",
                         _projectName + " collision"
                     })
            {
                var projectFile = AuxiliaryProjectSessionStore.GetStoredProjectFile(_sessionId, projectName);
                foreach (var file in new[]
                         {
                             projectFile,
                             Path.ChangeExtension(projectFile, ".fbdeps"),
                             Path.ChangeExtension(projectFile, ".fblocaldata")
                         })
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
            }
            FlowBloxOptions.GetOptionInstance().GetOption("Paths.LocalAppDataDir").Value = _previousLocalAppDataDirectory;
            if (Directory.Exists(_localAppDataDirectory))
                Directory.Delete(_localAppDataDirectory, recursive: true);
        }

        [TestMethod]
        public async Task CreateSaveGetAndEdit_PersistsProjectAndSwitchesRegistryScope()
        {
            var api = new DefaultToolApi();

            var created = await Execute(api, "CreateOrUpdateAuxiliaryProject", new JObject
            {
                ["projectName"] = _projectName,
                ["description"] = ProjectDescription,
                ["mode"] = "metadata_only"
            });
            var editing = await Execute(api, "AuxiliaryProjectEditing", new JObject
            {
                ["method"] = "EDIT",
                ["projectName"] = _projectName
            });
            var flowBlock = await Execute(api, "CreateFlowBlock", new JObject
            {
                ["typeFullName"] = typeof(StartFlowBlock).FullName,
                ["name"] = "AuxStart",
                ["x"] = 50,
                ["y"] = 400
            });
            var auxiliaryMetadata = await Execute(api, "GetProjectMetadata", new JObject());

            Assert.IsTrue(created.Ok, created.Error);
            Assert.IsTrue(editing.Ok, editing.Error);
            Assert.IsTrue(flowBlock.Ok, flowBlock.Error);
            var activeProject = await Execute(api, "GetAuxiliaryProject", new JObject
            {
                ["projectName"] = _projectName,
                ["mode"] = "session"
            });
            Assert.IsTrue(activeProject.Ok, activeProject.Error);
            Assert.IsTrue(activeProject.Result.Value<bool>("isOpen"));
            Assert.IsTrue(auxiliaryMetadata.Result.Value<bool>("isAuxiliaryProject"));
            Assert.AreEqual(_projectName, auxiliaryMetadata.Result.Value<string>("projectName"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(auxiliaryMetadata.Result.Value<string>("description")));
            Assert.AreEqual(0, FlowBloxProjectManager.Instance.ActiveProject.FlowBloxRegistry.GetFlowBlocks().Count());
            Assert.IsTrue(File.Exists(AuxiliaryProjectSessionStore.GetStoredProjectFile(_sessionId, _projectName)));

            var saved = await Execute(api, "AuxiliaryProjectEditing", new JObject
            {
                ["method"] = "SAVE",
                ["projectName"] = _projectName
            });
            var projects = await Execute(api, "GetAuxiliaryProjects", new JObject());
            var mainMetadata = await Execute(api, "GetProjectMetadata", new JObject());

            Assert.IsTrue(saved.Ok, saved.Error);
            Assert.IsTrue(saved.Result.Value<bool>("closed"));
            Assert.IsTrue(File.Exists(saved.Result.Value<string>("projectFile")));
            var discoveredProject = projects.Result["projects"]!.Single(x =>
                string.Equals(x!.Value<string>("projectName"), _projectName, StringComparison.Ordinal));
            Assert.IsFalse(string.IsNullOrWhiteSpace(discoveredProject!.Value<string>("description")));
            Assert.IsTrue(File.Exists(discoveredProject.Value<string>("projectFile")));
            var singleProject = await Execute(api, "GetAuxiliaryProject", new JObject
            {
                ["projectName"] = _projectName,
                ["mode"] = "session"
            });
            Assert.IsTrue(singleProject.Ok, singleProject.Error);
            Assert.IsTrue(JToken.DeepEquals(discoveredProject, singleProject.Result));
            StringAssert.Contains(File.ReadAllText(saved.Result.Value<string>("projectFile")), "AuxStart");
            Assert.IsFalse(mainMetadata.Result.Value<bool>("isAuxiliaryProject"));
            Assert.AreEqual("Main", mainMetadata.Result.Value<string>("projectName"));

            var secondSessionId = Guid.NewGuid().ToString("N");
            ToolHandlerUtilities.SetCurrentSessionGuid(secondSessionId);
            var projectsFromAnotherSession = await Execute(api, "GetAuxiliaryProjects", new JObject());
            Assert.IsFalse(projectsFromAnotherSession.Result["projects"]!.Any(x =>
                string.Equals(x!.Value<string>("projectName"), _projectName, StringComparison.Ordinal)));
            var singleFromAnotherSession = await Execute(api, "GetAuxiliaryProject", new JObject
            {
                ["projectName"] = _projectName,
                ["mode"] = "session"
            });
            Assert.IsFalse(singleFromAnotherSession.Ok);
            ToolHandlerUtilities.SetCurrentSessionGuid(_sessionId);

            var reopened = await Execute(api, "AuxiliaryProjectEditing", new JObject
            {
                ["method"] = "EDIT",
                ["projectName"] = _projectName
            });
            var editingMetadata = await Execute(api, "GetProjectMetadata", new JObject());
            var closed = await Execute(api, "AuxiliaryProjectEditing", new JObject { ["method"] = "CLOSE" });

            Assert.IsTrue(reopened.Ok, reopened.Error);
            Assert.IsTrue(editingMetadata.Result.Value<bool>("isAuxiliaryProject"));
            Assert.AreEqual(_projectName, editingMetadata.Result.Value<string>("projectName"));
            Assert.IsTrue(closed.Ok, closed.Error);
        }

        [TestMethod]
        [DataRow("BraveWebSearch.fbprj")]
        [DataRow("DeepSeekWebSearch.fbprj")]
        [DataRow("ListFiles.fbprj")]
        [DataRow("GetFileContent.fbprj")]
        public async Task Create_CopiesDistributedProject_AndUpdateRequiresClosedProject(string distributedProjectFile)
        {
            var api = new DefaultToolApi();
            var createWithoutDescription = await Execute(api, "CreateOrUpdateAuxiliaryProject", new JObject
            {
                ["projectName"] = _projectName + " without description",
                ["mode"] = "metadata_only"
            });
            var createResponse = await Execute(api, "CreateOrUpdateAuxiliaryProject", new JObject
            {
                ["projectName"] = _projectName,
                ["mode"] = "full_content",
                ["copyFromDistributedData"] = new JObject
                {
                    ["dataType"] = "auxiliary_project",
                    ["fileName"] = distributedProjectFile
                }
            });
            var opened = await Execute(api, "AuxiliaryProjectEditing", new JObject
            {
                ["method"] = "EDIT",
                ["projectName"] = _projectName
            });
            var updateWhileOpen = await Execute(api, "CreateOrUpdateAuxiliaryProject", new JObject
            {
                ["projectName"] = _projectName,
                ["mode"] = "full_content",
                ["projectJson"] = "{\"ProjectDescription\":\"Updated\",\"FlowBlocks\":[],\"ManagedObjects\":[],\"UserFields\":[],\"ProjectDependendDataObjects\":[],\"InputFiles\":[]}"
            });
            var saved = await Execute(api, "AuxiliaryProjectEditing", new JObject
            {
                ["method"] = "SAVE",
                ["projectName"] = _projectName
            });
            var updateAfterClose = await Execute(api, "CreateOrUpdateAuxiliaryProject", new JObject
            {
                ["projectName"] = _projectName,
                ["mode"] = "full_content",
                ["projectJson"] = "{\"ProjectDescription\":\"Updated\",\"FlowBlocks\":[],\"ManagedObjects\":[],\"UserFields\":[],\"ProjectDependendDataObjects\":[],\"InputFiles\":[]}"
            });

            Assert.IsFalse(createWithoutDescription.Ok);
            StringAssert.Contains(createWithoutDescription.Error, "description is required for metadata_only");
            Assert.IsTrue(createResponse.Ok, createResponse.Error);
            Assert.IsFalse(string.IsNullOrWhiteSpace(createResponse.Result.Value<string>("description")));
            Assert.IsTrue(opened.Ok, opened.Error);
            Assert.IsFalse(updateWhileOpen.Ok);
            StringAssert.Contains(updateWhileOpen.Error, "not allowed while an auxiliary project is open");
            Assert.IsTrue(saved.Ok, saved.Error);
            Assert.IsTrue(updateAfterClose.Ok, updateAfterClose.Error);
        }

        [TestMethod]
        [DataRow("executesqlquery")]
        [DataRow("GetFileContent")]
        public async Task GetAuxiliaryProject_ResolvesBootstrapNameWithSameParameterContractAsCatalog(string projectName)
        {
            var api = new DefaultToolApi();
            var single = await Execute(api, "GetAuxiliaryProject", new JObject
            {
                ["projectName"] = projectName
            });
            Assert.IsTrue(single.Ok, single.Error);
            var catalog = await Execute(api, "GetAuxiliaryProjects", new JObject
            {
                ["mode"] = "distributedData"
            });
            var listed = catalog.Result["projects"]!.Single(project => string.Equals(
                project!.Value<string>("projectName"), projectName, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(JToken.DeepEquals(listed, single.Result));
            Assert.IsTrue(File.Exists(single.Result.Value<string>("projectFile")));
            Assert.IsTrue(single.Result["inputFieldListOfValues"] is JObject);
            Assert.IsTrue(single.Result["outputFields"]!.Any());
        }

        [TestMethod]
        [DataRow("", "distributedData", "projectName is required")]
        [DataRow("ExecuteSQL", "distributedData", "was not found")]
        [DataRow("ExecuteSQLQuery", "session", "was not found")]
        [DataRow("ExecuteSQLQuery", "unknown", "not supported")]
        public async Task GetAuxiliaryProject_RejectsMissingPartialOrWrongSourceNames(string projectName, string mode, string expectedError)
        {
            var response = await Execute(new DefaultToolApi(), "GetAuxiliaryProject", new JObject
            {
                ["projectName"] = projectName,
                ["mode"] = mode
            });
            Assert.IsFalse(response.Ok);
            StringAssert.Contains(response.Error, expectedError);
        }

        [TestMethod]
        [DataRow("ExecuteExternalProject")]
        [DataRow("RunProjectDebugTest")]
        public async Task DistributedProject_CanBeSubmittedDirectlyForExecutionWithoutSessionCopy(string toolName)
        {
            ToolRequest? confirmation = null;
            var api = new DefaultToolApi
            {
                ToolExecutionConfirmationCallback = request =>
                {
                    confirmation = request;
                    return false;
                }
            };
            var catalog = await Execute(api, "GetAuxiliaryProjects", new JObject
            {
                ["mode"] = "distributedData",
                ["filter"] = "ExecuteSQLQuery"
            });
            Assert.IsTrue(catalog.Ok, catalog.Error);
            var project = catalog.Result["projects"]!.Children<JObject>().Single();
            var projectFile = project.Value<string>("projectFile");
            Assert.IsTrue(File.Exists(projectFile));

            var response = await Execute(api, toolName, new JObject
            {
                ["projectFile"] = projectFile,
                ["reason"] = "Inspect the database using the unchanged supplied project.",
                ["inputParameters"] = new JObject
                {
                    ["Input_DbType"] = "MSSQL",
                    ["Input_SQLConnectionString"] = "Server=localhost;Database=master;Integrated Security=True;",
                    ["Input_SQLQuery"] = "SELECT name FROM sys.databases;"
                }
            });

            Assert.IsNotNull(confirmation, response.Error);
            Assert.AreEqual(projectFile, confirmation.Arguments.Value<string>("projectFile"));
            Assert.AreEqual("SELECT name FROM sys.databases;", confirmation.ConfirmationInputParameters!["Input_SQLQuery"]);
            Assert.IsTrue(response.Result.Value<bool>("cancelledByUser"));
            var sessionProjects = await Execute(api, "GetAuxiliaryProjects", new JObject());
            Assert.AreEqual(0, sessionProjects.Result["projects"]!.Count());
        }

        [TestMethod]
        public async Task GetAuxiliaryProjects_ListsAndFiltersDistributedProjects()
        {
            var api = new DefaultToolApi();
            var allProjects = await Execute(api, "GetAuxiliaryProjects", new JObject
            {
                ["mode"] = "distributedData"
            });

            Assert.IsTrue(allProjects.Ok, allProjects.Error);
            var projects = allProjects.Result["projects"]!.Children<JObject>().ToList();
            Assert.IsTrue(projects.Count > 0);
            Assert.IsTrue(projects.All(x => !string.IsNullOrWhiteSpace(x.Value<string>("fileName"))));
            Assert.IsTrue(projects.All(x => x["inputFields"] is JArray && x["outputFields"] is JArray));

            var executeSqlQuery = projects.Single(x =>
                string.Equals(x.Value<string>("projectName"), "ExecuteSQLQuery", StringComparison.Ordinal));
            CollectionAssert.AreEquivalent(
                new[] { "Input_DbType", "Input_SQLConnectionString", "Input_SQLQuery" },
                executeSqlQuery["inputFields"]!.Values<string>().ToArray());
            CollectionAssert.AreEqual(
                new[] { "Content" },
                executeSqlQuery["outputFields"]!.Values<string>().ToArray());

            var expectedProject = projects.First(x =>
                (x.Value<string>("projectName") ?? string.Empty).Contains("MySql", StringComparison.OrdinalIgnoreCase));
            var filter = "mysql schema database columns tables";
            var filtered = await Execute(api, "GetAuxiliaryProjects", new JObject
            {
                ["mode"] = "distributedData",
                ["filter"] = filter
            });

            Assert.IsTrue(filtered.Ok, filtered.Error);
            var filterTerms = filtered.Result["filterTerms"]!.Values<string>().ToList();
            CollectionAssert.AreEquivalent(filter.Split(' ').ToList(), filterTerms);
            var filteredProjects = filtered.Result["projects"]!.Children<JObject>().ToList();
            Assert.IsTrue(filteredProjects.Any(x =>
                string.Equals(
                    x.Value<string>("fileName"),
                    expectedProject.Value<string>("fileName"),
                    StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(filteredProjects.All(x =>
                filterTerms.Any(term =>
                    (x.Value<string>("projectName") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (x.Value<string>("shortDescription") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (x.Value<string>("description") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))));
        }

        [TestMethod]
        public async Task MetadataUpdate_RenamesProjectFileAndRejectsExistingTarget()
        {
            var api = new DefaultToolApi();
            var collisionName = _projectName + " collision";
            var renamedName = _projectName + " renamed";
            foreach (var projectName in new[] { _projectName, collisionName })
            {
                var created = await Execute(api, "CreateOrUpdateAuxiliaryProject", new JObject
                {
                    ["projectName"] = projectName,
                    ["description"] = ProjectDescription,
                    ["mode"] = "metadata_only"
                });
                Assert.IsTrue(created.Ok, created.Error);
            }

            var collision = await Execute(api, "CreateOrUpdateAuxiliaryProject", new JObject
            {
                ["existingProjectName"] = _projectName,
                ["projectName"] = collisionName,
                ["description"] = "Renamed project.",
                ["mode"] = "metadata_only"
            });
            var renamed = await Execute(api, "CreateOrUpdateAuxiliaryProject", new JObject
            {
                ["existingProjectName"] = _projectName,
                ["projectName"] = renamedName,
                ["description"] = "Renamed project.",
                ["mode"] = "metadata_only"
            });

            Assert.IsFalse(collision.Ok);
            StringAssert.Contains(collision.Error, "already exists in the current session");
            Assert.IsTrue(renamed.Ok, renamed.Error);
            Assert.IsFalse(File.Exists(AuxiliaryProjectSessionStore.GetStoredProjectFile(_sessionId, _projectName)));
            Assert.IsTrue(File.Exists(AuxiliaryProjectSessionStore.GetStoredProjectFile(_sessionId, renamedName)));
            Assert.AreEqual(renamedName, renamed.Result.Value<string>("projectName"));
        }

        private static Task<ToolResponse> Execute(DefaultToolApi api, string toolName, JObject arguments) =>
            api.ExecuteAsync(new ToolRequest { ToolName = toolName, Arguments = arguments }, CancellationToken.None);
    }
}
