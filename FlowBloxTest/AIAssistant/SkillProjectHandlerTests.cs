using FlowBlox.Core.Constants;
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
    public sealed class SkillProjectHandlerTests
    {
        private readonly string _sessionId = Guid.NewGuid().ToString("N");
        private readonly string _skillProjectName = "Schema inspection " + Guid.NewGuid().ToString("N");
        private const string ProjectDescription = "Inspects a database schema for the current assistant session.";
        private string _previousLocalAppDataDirectory = null!;
        private string _localAppDataDirectory = null!;
        private string _previousApprovals = null!;

        [TestInitialize]
        public void Initialize()
        {
            var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsLocalAppDataDir);
            _previousLocalAppDataDirectory = option.Value;
            _localAppDataDirectory = Path.Combine(Path.GetTempPath(), "FlowBloxSkillTests", Guid.NewGuid().ToString("N"));
            option.Value = _localAppDataDirectory;
            var approvals = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIApprovedSkillProjects);
            _previousApprovals = approvals.PersistentValue;
            approvals.PersistentValue = string.Empty;
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject { ProjectName = "Main" };
            ToolHandlerUtilities.SetCurrentSessionGuid(_sessionId);
        }

        [TestCleanup]
        public void Cleanup()
        {
            FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIApprovedSkillProjects).PersistentValue = _previousApprovals;
            ToolHandlerUtilities.ClearSessionCache(_sessionId);
            ToolHandlerUtilities.SetCurrentSessionGuid(null);
            foreach (var skillProjectName in new[]
                     {
                         _skillProjectName,
                         _skillProjectName + " renamed",
                         _skillProjectName + " collision"
                     })
            {
                var projectFile = SkillProjectSessionStore.GetStoredProjectFile(_sessionId, skillProjectName);
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
            FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsLocalAppDataDir).Value = _previousLocalAppDataDirectory;
            if (Directory.Exists(_localAppDataDirectory))
                Directory.Delete(_localAppDataDirectory, recursive: true);
        }

        [TestMethod]
        public async Task CreateSaveGetAndEdit_PersistsProjectAndSwitchesRegistryScope()
        {
            var api = new DefaultToolApi();

            var created = await Execute(api, "CreateOrUpdateSkillProject", new JObject
            {
                ["skillProjectName"] = _skillProjectName,
                ["description"] = ProjectDescription,
                ["mode"] = "metadata_only"
            });
            var editing = await Execute(api, "SkillProjectEditing", new JObject
            {
                ["method"] = "EDIT",
                ["skillProjectName"] = _skillProjectName
            });
            var flowBlock = await Execute(api, "CreateFlowBlock", new JObject
            {
                ["typeFullName"] = typeof(StartFlowBlock).FullName,
                ["name"] = "SkillStart",
                ["x"] = 50,
                ["y"] = 400
            });
            var skillMetadata = await Execute(api, "GetProjectMetadata", new JObject());

            Assert.IsTrue(created.Ok, created.Error);
            Assert.IsTrue(editing.Ok, editing.Error);
            Assert.IsTrue(flowBlock.Ok, flowBlock.Error);
            var activeProject = await Execute(api, "GetSkillProject", new JObject
            {
                ["skillProjectName"] = _skillProjectName,
                ["mode"] = "session"
            });
            Assert.IsTrue(activeProject.Ok, activeProject.Error);
            Assert.IsTrue(activeProject.Result.Value<bool>("isOpen"));
            Assert.IsTrue(skillMetadata.Result.Value<bool>("isSkillProject"));
            Assert.AreEqual(_skillProjectName, skillMetadata.Result.Value<string>("projectName"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(skillMetadata.Result.Value<string>("description")));
            Assert.AreEqual(0, FlowBloxProjectManager.Instance.ActiveProject.FlowBloxRegistry.GetFlowBlocks().Count());
            Assert.IsTrue(File.Exists(SkillProjectSessionStore.GetStoredProjectFile(_sessionId, _skillProjectName)));

            var saved = await Execute(api, "SkillProjectEditing", new JObject
            {
                ["method"] = "SAVE",
                ["skillProjectName"] = _skillProjectName
            });
            var projects = await Execute(api, "GetSkillProjects", new JObject());
            var mainMetadata = await Execute(api, "GetProjectMetadata", new JObject());

            Assert.IsTrue(saved.Ok, saved.Error);
            Assert.IsTrue(saved.Result.Value<bool>("closed"));
            Assert.IsTrue(File.Exists(saved.Result.Value<string>("projectFile")));
            var discoveredProject = projects.Result["skillProjects"]!.Single(x =>
                string.Equals(x!.Value<string>("skillProjectName"), _skillProjectName, StringComparison.Ordinal));
            Assert.IsFalse(string.IsNullOrWhiteSpace(discoveredProject!.Value<string>("description")));
            Assert.IsTrue(File.Exists(discoveredProject.Value<string>("projectFile")));
            var singleProject = await Execute(api, "GetSkillProject", new JObject
            {
                ["skillProjectName"] = _skillProjectName,
                ["mode"] = "session"
            });
            Assert.IsTrue(singleProject.Ok, singleProject.Error);
            Assert.IsTrue(JToken.DeepEquals(discoveredProject, singleProject.Result));
            StringAssert.Contains(File.ReadAllText(saved.Result.Value<string>("projectFile")), "SkillStart");
            Assert.IsFalse(mainMetadata.Result.Value<bool>("isSkillProject"));
            Assert.AreEqual("Main", mainMetadata.Result.Value<string>("projectName"));

            var secondSessionId = Guid.NewGuid().ToString("N");
            ToolHandlerUtilities.SetCurrentSessionGuid(secondSessionId);
            var projectsFromAnotherSession = await Execute(api, "GetSkillProjects", new JObject());
            Assert.IsFalse(projectsFromAnotherSession.Result["skillProjects"]!.Any(x =>
                string.Equals(x!.Value<string>("skillProjectName"), _skillProjectName, StringComparison.Ordinal)));
            var singleFromAnotherSession = await Execute(api, "GetSkillProject", new JObject
            {
                ["skillProjectName"] = _skillProjectName,
                ["mode"] = "session"
            });
            Assert.IsFalse(singleFromAnotherSession.Ok);
            ToolHandlerUtilities.SetCurrentSessionGuid(_sessionId);

            var reopened = await Execute(api, "SkillProjectEditing", new JObject
            {
                ["method"] = "EDIT",
                ["skillProjectName"] = _skillProjectName
            });
            var editingMetadata = await Execute(api, "GetProjectMetadata", new JObject());
            var closed = await Execute(api, "SkillProjectEditing", new JObject { ["method"] = "CLOSE" });

            Assert.IsTrue(reopened.Ok, reopened.Error);
            Assert.IsTrue(editingMetadata.Result.Value<bool>("isSkillProject"));
            Assert.AreEqual(_skillProjectName, editingMetadata.Result.Value<string>("projectName"));
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
            var createWithoutDescription = await Execute(api, "CreateOrUpdateSkillProject", new JObject
            {
                ["skillProjectName"] = _skillProjectName + " without description",
                ["mode"] = "metadata_only"
            });
            var createResponse = await Execute(api, "CreateOrUpdateSkillProject", new JObject
            {
                ["skillProjectName"] = _skillProjectName,
                ["mode"] = "full_content",
                ["copyFromDistributedData"] = new JObject
                {
                    ["dataType"] = "skill_project",
                    ["fileName"] = distributedProjectFile
                }
            });
            var opened = await Execute(api, "SkillProjectEditing", new JObject
            {
                ["method"] = "EDIT",
                ["skillProjectName"] = _skillProjectName
            });
            var updateWhileOpen = await Execute(api, "CreateOrUpdateSkillProject", new JObject
            {
                ["skillProjectName"] = _skillProjectName,
                ["mode"] = "full_content",
                ["projectJson"] = "{\"ProjectDescription\":\"Updated\",\"FlowBlocks\":[],\"ManagedObjects\":[],\"UserFields\":[],\"ProjectDependendDataObjects\":[],\"InputFiles\":[]}"
            });
            var saved = await Execute(api, "SkillProjectEditing", new JObject
            {
                ["method"] = "SAVE",
                ["skillProjectName"] = _skillProjectName
            });
            var updateAfterClose = await Execute(api, "CreateOrUpdateSkillProject", new JObject
            {
                ["skillProjectName"] = _skillProjectName,
                ["mode"] = "full_content",
                ["projectJson"] = "{\"ProjectDescription\":\"Updated\",\"FlowBlocks\":[],\"ManagedObjects\":[],\"UserFields\":[],\"ProjectDependendDataObjects\":[],\"InputFiles\":[]}"
            });

            Assert.IsFalse(createWithoutDescription.Ok);
            StringAssert.Contains(createWithoutDescription.Error, "description is required for metadata_only");
            Assert.IsTrue(createResponse.Ok, createResponse.Error);
            Assert.IsFalse(string.IsNullOrWhiteSpace(createResponse.Result.Value<string>("description")));
            Assert.IsTrue(opened.Ok, opened.Error);
            Assert.IsFalse(updateWhileOpen.Ok);
            StringAssert.Contains(updateWhileOpen.Error, "not allowed while a skill project is open");
            Assert.IsTrue(saved.Ok, saved.Error);
            Assert.IsTrue(updateAfterClose.Ok, updateAfterClose.Error);
        }

        [TestMethod]
        [DataRow("executesqlquery")]
        [DataRow("GetFileContent")]
        [DataRow("SearchJiraTickets")]
        [DataRow("GetJiraTicket")]
        public async Task GetSkillProject_ResolvesBootstrapNameWithSameParameterContractAsCatalog(string skillProjectName)
        {
            var api = new DefaultToolApi();
            var single = await Execute(api, "GetSkillProject", new JObject
            {
                ["skillProjectName"] = skillProjectName
            });
            Assert.IsTrue(single.Ok, single.Error);
            var catalog = await Execute(api, "GetSkillProjects", new JObject
            {
                ["mode"] = "distributedData"
            });
            var listed = catalog.Result["skillProjects"]!.Single(project => string.Equals(
                project!.Value<string>("skillProjectName"), skillProjectName, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(JToken.DeepEquals(listed, single.Result));
            Assert.IsTrue(File.Exists(single.Result.Value<string>("projectFile")));
            Assert.IsTrue(single.Result["inputFieldListOfValues"] is JObject);
            Assert.IsTrue(single.Result["outputFields"]!.Any());
        }

        [TestMethod]
        public async Task GetSkillProject_ReportsStartupRequirementsAndValueListSemantics()
        {
            var api = new DefaultToolApi();
            var file = await Execute(api, "GetSkillProject", new JObject { ["skillProjectName"] = "GetFileContent" });
            Assert.IsTrue(file.Ok, file.Error);
            var metadata = file.Result["inputFieldMetadata"]!;
            Assert.IsTrue(metadata["FilePath"]!.Value<bool>("isRequiredAtRuntimeStartup"));
            Assert.AreEqual("PossibleValues", metadata["InputFileType"]!.Value<string>("loVType"));
            Assert.AreEqual("Suggestion", metadata["Encoding"]!.Value<string>("loVType"));
            Assert.IsTrue(file.Result["memoryFieldMetadata"]!["CsvContent"]!.Value<bool>("clearOnRuntimeStart"));
            Assert.IsFalse(file.Result["memoryFieldMetadata"]!["CsvContent"]!.Value<bool>("isRequiredAtRuntimeStartup"));

            var auth = await Execute(api, "GetSkillProject", new JObject { ["skillProjectName"] = "FetchHtmlWithOpenId" });
            Assert.IsTrue(auth.Ok, auth.Error);
            Assert.IsTrue(auth.Result["inputFieldMetadata"]!["Password"]!.Value<bool>("isPassword"));
            Assert.IsTrue(auth.Result["inputFieldMetadata"]!["Password"]!.Value<bool>("isRequiredAtRuntimeStartup"));
            Assert.IsFalse(auth.Result["inputFieldMetadata"]!["ClientSecret"]!.Value<bool>("isRequiredAtRuntimeStartup"));
            Assert.IsFalse(auth.Result["inputFieldMetadata"]!["Scope"]!.Value<bool>("isRequiredAtRuntimeStartup"));
            Assert.IsFalse(auth.Result["inputFieldMetadata"]!.SelectTokens("$..StringValue").Any());
        }

        [TestMethod]
        [DataRow("", "distributedData", "skillProjectName is required")]
        [DataRow("ExecuteSQL", "distributedData", "was not found")]
        [DataRow("ExecuteSQLQuery", "session", "was not found")]
        [DataRow("ExecuteSQLQuery", "unknown", "not supported")]
        public async Task GetSkillProject_RejectsMissingPartialOrWrongSourceNames(string skillProjectName, string mode, string expectedError)
        {
            var response = await Execute(new DefaultToolApi(), "GetSkillProject", new JObject
            {
                ["skillProjectName"] = skillProjectName,
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
            var catalog = await Execute(api, "GetSkillProjects", new JObject
            {
                ["mode"] = "distributedData",
                ["filter"] = "ExecuteSQLQuery"
            });
            Assert.IsTrue(catalog.Ok, catalog.Error);
            var project = catalog.Result["skillProjects"]!.Children<JObject>().Single();
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
            var sessionProjects = await Execute(api, "GetSkillProjects", new JObject());
            Assert.AreEqual(0, sessionProjects.Result["skillProjects"]!.Count());
        }

        [TestMethod]
        public async Task GetSkillProjects_ListsAndFiltersDistributedProjects()
        {
            var api = new DefaultToolApi();
            var allProjects = await Execute(api, "GetSkillProjects", new JObject
            {
                ["mode"] = "distributedData"
            });

            Assert.IsTrue(allProjects.Ok, allProjects.Error);
            var projects = allProjects.Result["skillProjects"]!.Children<JObject>().ToList();
            Assert.IsTrue(projects.Count > 0);
            Assert.IsTrue(projects.All(x => !string.IsNullOrWhiteSpace(x.Value<string>("fileName"))));
            Assert.IsTrue(projects.All(x => x["inputFields"] is JArray && x["outputFields"] is JArray));

            var executeSqlQuery = projects.Single(x =>
                string.Equals(x.Value<string>("skillProjectName"), "ExecuteSQLQuery", StringComparison.Ordinal));
            CollectionAssert.AreEquivalent(
                new[] { "Input_DbType", "Input_SQLConnectionString", "Input_SQLQuery" },
                executeSqlQuery["inputFields"]!.Values<string>().ToArray());
            CollectionAssert.AreEqual(
                new[] { "Content" },
                executeSqlQuery["outputFields"]!.Values<string>().ToArray());

            var expectedProject = projects.First(x =>
                (x.Value<string>("skillProjectName") ?? string.Empty).Contains("MySql", StringComparison.OrdinalIgnoreCase));
            var filter = "mysql schema database columns tables";
            var filtered = await Execute(api, "GetSkillProjects", new JObject
            {
                ["mode"] = "distributedData",
                ["filter"] = filter
            });

            Assert.IsTrue(filtered.Ok, filtered.Error);
            var filterTerms = filtered.Result["filterTerms"]!.Values<string>().ToList();
            CollectionAssert.AreEquivalent(filter.Split(' ').ToList(), filterTerms);
            var filteredProjects = filtered.Result["skillProjects"]!.Children<JObject>().ToList();
            Assert.IsTrue(filteredProjects.Any(x =>
                string.Equals(
                    x.Value<string>("fileName"),
                    expectedProject.Value<string>("fileName"),
                    StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(filteredProjects.All(x =>
                filterTerms.Any(term =>
                    (x.Value<string>("skillProjectName") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (x.Value<string>("shortDescription") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (x.Value<string>("description") ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))));
        }

        [TestMethod]
        public async Task MetadataUpdate_RenamesProjectFileAndRejectsExistingTarget()
        {
            var api = new DefaultToolApi();
            var collisionName = _skillProjectName + " collision";
            var renamedName = _skillProjectName + " renamed";
            foreach (var skillProjectName in new[] { _skillProjectName, collisionName })
            {
                var created = await Execute(api, "CreateOrUpdateSkillProject", new JObject
                {
                    ["skillProjectName"] = skillProjectName,
                    ["description"] = ProjectDescription,
                    ["mode"] = "metadata_only"
                });
                Assert.IsTrue(created.Ok, created.Error);
            }

            var collision = await Execute(api, "CreateOrUpdateSkillProject", new JObject
            {
                ["existingSkillProjectName"] = _skillProjectName,
                ["skillProjectName"] = collisionName,
                ["description"] = "Renamed project.",
                ["mode"] = "metadata_only"
            });
            var renamed = await Execute(api, "CreateOrUpdateSkillProject", new JObject
            {
                ["existingSkillProjectName"] = _skillProjectName,
                ["skillProjectName"] = renamedName,
                ["description"] = "Renamed project.",
                ["mode"] = "metadata_only"
            });

            Assert.IsFalse(collision.Ok);
            StringAssert.Contains(collision.Error, "already exists in the current session");
            Assert.IsTrue(renamed.Ok, renamed.Error);
            Assert.IsFalse(File.Exists(SkillProjectSessionStore.GetStoredProjectFile(_sessionId, _skillProjectName)));
            Assert.IsTrue(File.Exists(SkillProjectSessionStore.GetStoredProjectFile(_sessionId, renamedName)));
            Assert.AreEqual(renamedName, renamed.Result.Value<string>("skillProjectName"));
        }

        private static Task<ToolResponse> Execute(DefaultToolApi api, string toolName, JObject arguments) =>
            api.ExecuteAsync(new ToolRequest { ToolName = toolName, Arguments = arguments }, CancellationToken.None);
    }
}
