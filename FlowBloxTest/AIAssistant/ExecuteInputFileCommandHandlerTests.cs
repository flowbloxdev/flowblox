using FlowBlox.Core.Constants;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;
using System.Text;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [DoNotParallelize]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class ExecuteInputFileCommandHandlerTests
    {
        private string _temporaryInputRoot = null!;
        private string _previousInputRoot = null!;
        private FlowBloxProject? _previousProject;

        [TestInitialize]
        public void Initialize()
        {
            _temporaryInputRoot = Path.Combine(Path.GetTempPath(), "FlowBloxAiInputFileTests", Guid.NewGuid().ToString("N"));
            var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsInputDir)
                ?? throw new AssertFailedException("Paths.InputDir option is missing.");
            _previousInputRoot = option.Value;
            option.Value = _temporaryInputRoot;
            _previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        }

        [TestCleanup]
        public void Cleanup()
        {
            FlowBloxProjectManager.Instance.ActiveProject = _previousProject;

            var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsInputDir);
            if (option != null)
                option.Value = _previousInputRoot;

            if (Directory.Exists(_temporaryInputRoot))
                Directory.Delete(_temporaryInputRoot, recursive: true);
        }

        [TestMethod]
        public async Task CreateInputFile_CopiesDistributedPythonData()
        {
            var project = new FlowBloxProject { ProjectName = "Distributed input file test" };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var response = await new DefaultToolApi().ExecuteAsync(
                new ToolRequest
                {
                    ToolName = "CreateOrUpdateInputFile",
                    Arguments = new JObject
                    {
                        ["key"] = "scripts/export_onnx_qa_model.py",
                        ["copyFromDistributedData"] = new JObject
                        {
                            ["dataType"] = "python",
                            ["fileName"] = "export_onnx_qa_model.py"
                        }
                    }
                },
                CancellationToken.None);

            Assert.IsTrue(response.Ok, response.Error);
            Assert.AreEqual(1, project.InputFiles.Count);
            StringAssert.Contains(Encoding.UTF8.GetString(project.InputFiles[0].ContentBytes), "DEFAULT_MODEL_ROOT");
        }

        [TestMethod]
        public async Task ExecuteInputFileCommand_MaterializesMissingScriptBeforeExecution()
        {
            var inputFile = new FlowBloxInputFile
            {
                RelativePath = "scripts/model.py",
                ContentBytes = Encoding.UTF8.GetBytes("print('ready')"),
                Command = "echo \"$InputFile::Path\"",
                SyncMode = FlowBloxInputFileSyncMode.CreateIfNotExists
            };
            var project = new FlowBloxProject
            {
                ProjectName = "AI Input File Command Test",
                InputFiles = [inputFile]
            };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var targetPath = FlowBloxInputFileHelper.BuildAbsoluteTargetPath(project.ProjectInputDirectory, inputFile.RelativePath);
            if (File.Exists(targetPath))
                File.Delete(targetPath);

            var api = new DefaultToolApi
            {
                ToolExecutionConfirmationCallback = _ => true
            };

            Assert.IsFalse(File.Exists(targetPath));

            var response = await api.ExecuteAsync(
                new ToolRequest
                {
                    ToolName = "ExecuteInputFileCommand",
                    Arguments = new JObject { ["key"] = inputFile.RelativePath }
                },
                CancellationToken.None);

            Assert.IsTrue(response.Ok, response.Error);
            Assert.IsTrue(File.Exists(targetPath));
            Assert.AreEqual("print('ready')", File.ReadAllText(targetPath));
            StringAssert.Contains(response.Result.Value<string>("command"), targetPath);
        }
    }
}
