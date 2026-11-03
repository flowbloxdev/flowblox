using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.ShellExecution;
using FlowBlox.Test.Runtime;
using System.Diagnostics;

namespace FlowBloxTest.FlowBlocks.AI
{
    // These long-running integration tests automatically download the required QA models.
    // Run them manually only; AI coding assistants and automated unit-test runs must not execute them.
    [TestClass]
    [DoNotParallelize]
    [TestCategory(FlowBloxTestCategories.IntegrationTests)]
    public class OnnxQAExecutionTests : FlowBloxTestsBase
    {
        private const string PublishedExportScriptName = "export_onnx_qa_model.py";
        private static string ModelRoot => FlowBloxOptions.GetOptionInstance()
            .GetOption(OnnxQAFlowBlock.ModelRootDirectoryOptionName)?.Value
            ?? throw new InvalidOperationException("The ONNX QA model root option is unavailable.");

        private FlowBloxProject _project = null!;

        public TestContext TestContext { get; set; } = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = _project;
        }

        [TestMethod]
        public void MDebertaV3MultilingualSquad2_AnswersQuestionAboutPersonName()
        {
            RunModelAndVerifyAnswer(
                modelId: "timpal0l/mdeberta-v3-base-squad2",
                folderName: "mdeberta-v3-base-squad2",
                question: "What is the employee's full name?",
                context: "The employee's full name is Ada Lovelace. She works in the research department.",
                expectedAnswer: "Ada Lovelace",
                sentencePieceSource: "microsoft/mdeberta-v3-base");
        }

        [TestMethod]
        public void DebertaV3BaseSquad2_AnswersQuestionAboutPersonName()
        {
            RunModelAndVerifyAnswer(
                modelId: "deepset/deberta-v3-base-squad2",
                folderName: "deberta-v3-base-squad2",
                question: "What is the employee's full name?",
                context: "The employee's full name is Ada Lovelace. She works in the research department.",
                expectedAnswer: "Ada Lovelace");
        }

        [TestMethod]
        public void XlmRobertaBaseSquad2_ReadsArticleNumberFromStructuredText()
        {
            RunModelAndVerifyAnswer(
                modelId: "deepset/xlm-roberta-base-squad2",
                folderName: "xlm-roberta-base-squad2",
                question: "What is the article number?",
                context: "Invoice: { Article number: AX-2048, Quantity: 3, Total amount: EUR 149.90 }",
                expectedAnswer: "AX-2048",
                allowContainingAnswer: true);
        }

        [TestMethod]
        public void GelectraGermanquad_ReadsInvoiceAmountFromText()
        {
            RunModelAndVerifyAnswer(
                modelId: "deepset/gelectra-base-germanquad",
                folderName: "gelectra-base-germanquad",
                question: "What is the total amount?",
                context: "The invoice for article AX-2048 has a total amount of EUR 149.90.",
                expectedAnswer: "EUR 149.90");
        }

        private void RunModelAndVerifyAnswer(
            string modelId,
            string folderName,
            string question,
            string context,
            string expectedAnswer,
            string? sentencePieceSource = null,
            bool allowContainingAnswer = false)
        {
            var modelFolder = Path.Combine(ModelRoot, folderName);
            ExportModelIfNeeded(modelId, modelFolder, sentencePieceSource);

            var start = CreateFlowBlock<StartFlowBlock>();
            var qa = CreateFlowBlock<OnnxQAFlowBlock>(start);
            qa.ModelFolder = modelFolder;
            qa.Question = question;
            qa.Context = context;
            qa.AllowNoAnswer = false;

            EnsureCpuOnnxRuntimeAvailable();
            var runtime = new FlowBloxUnitTestRuntime(_project);
            runtime.Execute();

            var actual = qa.GridElementResult.Results
                .SelectMany(result => result.FieldValueMappings)
                .Select(mapping => mapping.Value)
                .Single();
            if (allowContainingAnswer)
                StringAssert.Contains(actual, expectedAnswer);
            else
                Assert.AreEqual(expectedAnswer, actual);
        }

        private static void EnsureCpuOnnxRuntimeAvailable()
        {
            var target = Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "onnxruntimes",
                "cpu",
                "win-x64");
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var source = Path.Combine(
                    directory.FullName,
                    "FlowBloxResources",
                    "data",
                    "onnxruntimes",
                    "cpu",
                    "win-x64");
                if (Directory.Exists(source))
                {
                    OnnxRuntimeTestFileHelper.EnsureDirectory(source, target);
                    return;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "The win-x64 CPU ONNX Runtime could not be found in FlowBloxResources.");
        }

        private void ExportModelIfNeeded(
            string modelId,
            string modelFolder,
            string? sentencePieceSource)
        {
            if (IsCompleteModelFolder(modelFolder))
            {
                TestContext.WriteLine($"Using existing model: {modelFolder}");
                return;
            }

            var python = FindPythonOrFailTest();
            var scriptPath = FindPublishedExportScript();
            Directory.CreateDirectory(modelFolder);

            var arguments = new List<string>();
            arguments.AddRange(python.PrefixArguments);
            arguments.Add(scriptPath);
            arguments.Add("--model-id");
            arguments.Add(modelId);
            arguments.Add("--model-root-directory");
            arguments.Add(ModelRoot);
            if (!string.IsNullOrWhiteSpace(sentencePieceSource))
            {
                arguments.Add("--sentencepiece-source");
                arguments.Add(sentencePieceSource);
            }

            var command = string.Join(" ", new[] { python.Executable }
                .Concat(arguments)
                .Select(QuoteCommandArgument));
            TestContext.WriteLine($"Starting published QA export script:{Environment.NewLine}{command}");

            var result = FlowBloxShellExecutor.Execute(new FlowBloxShellExecutionRequest
            {
                Command = command,
                WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
                TimeoutMilliseconds = (int)TimeSpan.FromHours(1).TotalMilliseconds
            });

            LogScriptResult(result);
            if (!result.Success)
                Assert.Fail(CreateScriptFailureMessage(modelId, result));

            Assert.IsTrue(
                IsCompleteModelFolder(modelFolder),
                $"The export script completed successfully, but '{modelFolder}' does not contain a complete ONNX QA model with a tokenizer.");
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

        private static string CreateScriptFailureMessage(
            string modelId,
            FlowBloxShellExecutionResult result)
        {
            return $"ONNX export failed for '{modelId}'.{Environment.NewLine}" +
                   $"ExitCode: {result.ExitCode}{Environment.NewLine}" +
                   $"Timed out: {result.TimedOut}{Environment.NewLine}" +
                   $"Process error: {result.ExceptionMessage}{Environment.NewLine}" +
                   $"Standard output:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
                   $"Standard error:{Environment.NewLine}{result.StandardError}";
        }

        private static bool IsCompleteModelFolder(string modelFolder)
        {
            if (!File.Exists(Path.Combine(modelFolder, "model.onnx")) ||
                !File.Exists(Path.Combine(modelFolder, "config.json")) ||
                !File.Exists(Path.Combine(modelFolder, "tokenizer_config.json")))
                return false;

            var hasSingleFileTokenizer = new[]
            {
                "vocab.txt",
                "spm.model",
                "sentencepiece.bpe.model",
                "tokenizer.model"
            }.Any(name => File.Exists(Path.Combine(modelFolder, name)));
            var hasByteLevelBpe =
                File.Exists(Path.Combine(modelFolder, "vocab.json")) &&
                File.Exists(Path.Combine(modelFolder, "merges.txt"));
            return hasSingleFileTokenizer || hasByteLevelBpe;
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
                "Python 3 is required for the ONNX QA integration tests. " +
                "Install Python from https://www.python.org/downloads/, enable 'Add Python to PATH', and run the test again.");
            throw new InvalidOperationException("Assert.Fail did not terminate the test.");
        }

        private static string FindPublishedExportScript()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "FlowBlox.Build",
                    "data",
                    "python",
                    PublishedExportScriptName);
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            throw new AssertFailedException(
                $"The published QA export script '{PublishedExportScriptName}' could not be found from " +
                $"'{AppContext.BaseDirectory}' under FlowBlox.Build/data/python.");
        }

        private static string QuoteCommandArgument(string value) =>
            $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
