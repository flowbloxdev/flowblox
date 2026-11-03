using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.FlowBlocks.AI.TokenSelector;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.ShellExecution;
using FlowBlox.Test.Runtime;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace FlowBloxTest.FlowBlocks.AI
{
    // These long-running integration tests automatically download the required GenAI models.
    // Run them manually only; AI coding assistants and automated unit-test runs must not execute them.
    [TestClass]
    [DoNotParallelize]
    [TestCategory(FlowBloxTestCategories.IntegrationTests)]
    public class OnnxGenAIExecutionTests : FlowBloxTestsBase
    {
        private const string PublishedDownloadScriptName = "download_phi4_mini_instruct_onnx.py";
        private static string ModelRoot => FlowBloxOptions.GetOptionInstance()
            .GetOption(OnnxGenAIFlowBlock.ModelRootDirectoryOptionName)?.Value
            ?? throw new InvalidOperationException("The ONNX GenAI model root option is unavailable.");

        private static string ModelFolder => Path.Combine(
            ModelRoot,
            OnnxGenAIFlowBlock.DefaultModelFolderName);

        private FlowBloxProject _project = null!;

        public TestContext TestContext { get; set; } = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = _project;
        }

        [TestMethod]
        public void Phi4MiniInstruct_ExtractsParticipantDataFromMinimalHtmlAsJson()
        {
            DownloadModelIfNeeded();
            EnsureCpuRuntimeAvailable("onnxruntimes");
            EnsureCpuRuntimeAvailable("onnxruntimesgenai");

            const string html = """
                <ul>
                  <li data-first-name="Ada" data-last-name="Lovelace">Ada Lovelace</li>
                  <li data-first-name="Alan" data-last-name="Turing">Alan Turing</li>
                </ul>

                """;

            var start = CreateFlowBlock<StartFlowBlock>();
            var genAi = CreateFlowBlock<OnnxGenAIFlowBlock>(start);
            genAi.ModelFolder = ModelFolder;
            genAi.UseChatTemplate = true;
            genAi.SystemPrompt = "You extract structured data and return valid JSON only.";
            genAi.ChatTemplate = "<|system|>{SystemPrompt}<|end|><|user|>{UserPrompt} <|end|><|assistant|>";
            genAi.TokenSelectionStrategy = TokenSelectionStrategy.ArgMax;
            genAi.MaxNewTokens = 160;
            genAi.Prompt = $$"""
                Extract the participant data from the HTML below.
                Return JSON only, using exactly this schema:
                {"participants":[{"firstName":"","lastName":""}]}

                {{html}}
                """;

            var runtime = new FlowBloxUnitTestRuntime(_project);
            runtime.Execute();

            var json = ExtractJsonObject(genAi.ResultField.StringValue);
            var participants = (JArray?)json["participants"];

            Assert.IsNotNull(participants, "The generated JSON has no participants array.");
            Assert.AreEqual(2, participants.Count);
            Assert.AreEqual("Ada", participants[0]?["firstName"]?.Value<string>());
            Assert.AreEqual("Lovelace", participants[0]?["lastName"]?.Value<string>());
            Assert.AreEqual("Alan", participants[1]?["firstName"]?.Value<string>());
            Assert.AreEqual("Turing", participants[1]?["lastName"]?.Value<string>());
        }

        private void DownloadModelIfNeeded()
        {
            if (IsCompleteModelFolder(ModelFolder))
            {
                TestContext.WriteLine($"Using existing model: {ModelFolder}");
                return;
            }

            var python = FindPythonOrFailTest();
            var scriptPath = FindPublishedDownloadScript();
            Directory.CreateDirectory(ModelFolder);

            var arguments = python.PrefixArguments
                .Concat([scriptPath, "--model-root-directory", ModelRoot]);
            var command = string.Join(" ", new[] { python.Executable }
                .Concat(arguments)
                .Select(QuoteCommandArgument));
            TestContext.WriteLine($"Starting published Phi-4 Mini Instruct download script:{Environment.NewLine}{command}");

            var result = FlowBloxShellExecutor.Execute(new FlowBloxShellExecutionRequest
            {
                Command = command,
                WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
                TimeoutMilliseconds = (int)TimeSpan.FromHours(4).TotalMilliseconds
            });

            LogScriptResult(result);
            if (!result.Success)
                Assert.Fail(CreateScriptFailureMessage(result));

            Assert.IsTrue(
                IsCompleteModelFolder(ModelFolder),
                $"The download script completed successfully, but '{ModelFolder}' does not contain a complete Phi-4 Mini Instruct model.");
        }

        private static JObject ExtractJsonObject(string output)
        {
            var firstBrace = output?.IndexOf('{') ?? -1;
            var lastBrace = output?.LastIndexOf('}') ?? -1;
            Assert.IsTrue(
                firstBrace >= 0 && lastBrace > firstBrace,
                $"The model did not return a JSON object. Output:{Environment.NewLine}{output}");

            try
            {
                return JObject.Parse(output![firstBrace..(lastBrace + 1)]);
            }
            catch (Exception exception)
            {
                Assert.Fail($"The model output is not valid JSON: {exception.Message}{Environment.NewLine}{output}");
                throw;
            }
        }

        private static bool IsCompleteModelFolder(string modelFolder) =>
            new[] { "genai_config.json", "tokenizer.json", "model.onnx", "model.onnx.data" }
                .All(name => File.Exists(Path.Combine(modelFolder, name)));

        private static void EnsureCpuRuntimeAvailable(string runtimeDirectoryName)
        {
            var relativePath = Path.Combine(runtimeDirectoryName, "cpu", "win-x64");
            var target = Path.Combine(AppContext.BaseDirectory, "data", relativePath);
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                var source = Path.Combine(directory.FullName, "FlowBloxResources", "data", relativePath);
                if (Directory.Exists(source))
                {
                    OnnxRuntimeTestFileHelper.EnsureDirectory(source, target);
                    return;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"The win-x64 CPU runtime '{runtimeDirectoryName}' could not be found in FlowBloxResources.");
        }

        private static (string Executable, string[] PrefixArguments) FindPythonOrFailTest()
        {
            foreach (var candidate in new[]
                     {
                         (Executable: "python", PrefixArguments: Array.Empty<string>()),
                         (Executable: "py", PrefixArguments: new[] { "-3" })
                     })
            {
                try
                {
                    var info = new ProcessStartInfo
                    {
                        FileName = candidate.Executable,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    foreach (var argument in candidate.PrefixArguments)
                        info.ArgumentList.Add(argument);
                    info.ArgumentList.Add("--version");

                    using var process = Process.Start(info);
                    if (process != null && process.WaitForExit(15_000) && process.ExitCode == 0)
                        return candidate;
                }
                catch
                {
                    // Try the next common Python launcher.
                }
            }

            Assert.Fail(
                "Python 3 is required for the ONNX GenAI integration tests. " +
                "Install Python from https://www.python.org/downloads/, enable 'Add Python to PATH', and run the test again.");
            throw new InvalidOperationException("Assert.Fail did not terminate the test.");
        }

        private static string FindPublishedDownloadScript()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "FlowBlox.Build",
                    "data",
                    "python",
                    PublishedDownloadScriptName);
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            throw new AssertFailedException(
                $"The published Phi-4 Mini Instruct download script '{PublishedDownloadScriptName}' could not be found from " +
                $"'{AppContext.BaseDirectory}' under FlowBlox.Build/data/python.");
        }

        private void LogScriptResult(FlowBloxShellExecutionResult result)
        {
            TestContext.WriteLine($"ExitCode: {result.ExitCode}");
            TestContext.WriteLine($"Timed out: {result.TimedOut}");
            TestContext.WriteLine($"Standard output:{Environment.NewLine}{result.StandardOutput}");
            TestContext.WriteLine($"Standard error:{Environment.NewLine}{result.StandardError}");
            if (!string.IsNullOrWhiteSpace(result.ExceptionMessage))
                TestContext.WriteLine($"Process error: {result.ExceptionMessage}");
        }

        private static string CreateScriptFailureMessage(FlowBloxShellExecutionResult result) =>
            $"Phi-4 Mini Instruct download failed.{Environment.NewLine}" +
            $"ExitCode: {result.ExitCode}{Environment.NewLine}" +
            $"Timed out: {result.TimedOut}{Environment.NewLine}" +
            $"Process error: {result.ExceptionMessage}{Environment.NewLine}" +
            $"Standard output:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
            $"Standard error:{Environment.NewLine}{result.StandardError}";

        private static string QuoteCommandArgument(string value) =>
            $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
