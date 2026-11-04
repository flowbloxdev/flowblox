using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [DoNotParallelize]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class RunProjectDebugTestHandlerTests
    {
        [TestMethod]
        public async Task Confirmation_IsRequiredForSkillAndExplicitExternalProjects()
        {
            var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
            var localAppDataOption = FlowBloxOptions.GetOptionInstance().GetOption("Paths.LocalAppDataDir");
            var previousLocalAppDataDirectory = localAppDataOption.PersistentValue;
            var testDirectory = Path.Combine(Path.GetTempPath(), "FlowBloxTest", Guid.NewGuid().ToString("N"));
            var sessionId = Guid.NewGuid().ToString("N");
            localAppDataOption.PersistentValue = testDirectory;
            ToolHandlerUtilities.SetCurrentSessionGuid(sessionId);
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject { ProjectName = "Current" };
            var confirmationRequests = 0;
            var api = new DefaultToolApi
            {
                ToolExecutionConfirmationCallback = _ =>
                {
                    confirmationRequests++;
                    return false;
                }
            };

            try
            {
                var currentProjectResponse = await api.ExecuteAsync(
                    new ToolRequest
                    {
                        ToolName = "RunProjectDebugTest",
                        Arguments = new JObject { ["targetFlowBlockName"] = "Missing target" }
                    },
                    CancellationToken.None);
                Assert.AreEqual(0, confirmationRequests);
                Assert.IsFalse(currentProjectResponse.Result.Value<bool?>("cancelledByUser") == true);

                SkillProjectSessionStore.CreateOrUpdateFile(
                    sessionId,
                    "Skill",
                    new FlowBloxProject
                    {
                        ProjectName = "Skill",
                        ProjectDescription = "Security confirmation test"
                    });
                SkillProjectSessionStore.Edit(sessionId, "Skill");

                var skillResponse = await api.ExecuteAsync(
                    new ToolRequest
                    {
                        ToolName = "RunProjectDebugTest",
                        Arguments = new JObject { ["reason"] = "Inspect skill data" }
                    },
                    CancellationToken.None);
                Assert.AreEqual(1, confirmationRequests);
                Assert.IsTrue(skillResponse.Result.Value<bool>("cancelledByUser"));
                SkillProjectSessionStore.Close(sessionId);

                var externalProjectResponse = await api.ExecuteAsync(
                    new ToolRequest
                    {
                        ToolName = "RunProjectDebugTest",
                        Arguments = new JObject
                        {
                            ["projectFile"] = "external-project.fbprj",
                            ["reason"] = "Inspect external data"
                        }
                    },
                    CancellationToken.None);
                Assert.AreEqual(2, confirmationRequests);
                Assert.IsTrue(externalProjectResponse.Result.Value<bool>("cancelledByUser"));
            }
            finally
            {
                SkillProjectSessionStore.Clear(sessionId);
                ToolHandlerUtilities.SetCurrentSessionGuid(null);
                FlowBloxProjectManager.Instance.ActiveProject = previousProject;
                localAppDataOption.PersistentValue = previousLocalAppDataDirectory;
                if (Directory.Exists(testDirectory))
                    Directory.Delete(testDirectory, recursive: true);
            }
        }
    }
}
