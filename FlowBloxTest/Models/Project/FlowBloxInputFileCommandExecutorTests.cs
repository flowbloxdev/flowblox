using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util;

namespace FlowBloxTest.Models.Project
{
    [TestClass]
    [DoNotParallelize]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class FlowBloxInputFileCommandExecutorTests
    {
        private string _temporaryInputRoot = null!;
        private string _previousInputRoot = null!;

        [TestInitialize]
        public void Initialize()
        {
            _temporaryInputRoot = Path.Combine(Path.GetTempPath(), "FlowBloxInputFileTests", Guid.NewGuid().ToString("N"));
            var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsInputDir)
                ?? throw new AssertFailedException("Paths.InputDir option is missing.");
            _previousInputRoot = option.Value;
            option.Value = _temporaryInputRoot;
        }

        [TestCleanup]
        public void Cleanup()
        {
            var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsInputDir);
            if (option != null)
                option.Value = _previousInputRoot;

            if (Directory.Exists(_temporaryInputRoot))
                Directory.Delete(_temporaryInputRoot, recursive: true);
        }

        [TestMethod]
        public void Execute_EnsuresSelectedInputFileBeforeStartingCommand()
        {
            var inputFile = CreateInputFile(FlowBloxInputFileSyncMode.CreateIfNotExists, "print('ready')");
            var project = CreateProject(inputFile);
            var targetPath = FlowBloxInputFileHelper.BuildAbsoluteTargetPath(project.ProjectInputDirectory, inputFile.RelativePath);

            Assert.IsFalse(Directory.Exists(project.ProjectInputDirectory));

            var result = FlowBloxInputFileCommandExecutor.Execute(project, inputFile);

            Assert.IsTrue(result.Success, result.ExceptionMessage + result.StandardError);
            Assert.IsTrue(File.Exists(targetPath));
            Assert.AreEqual("print('ready')", File.ReadAllText(targetPath));
            StringAssert.Contains(result.Command, targetPath);
            Assert.AreEqual(project.ProjectInputDirectory, result.WorkingDirectory);
        }

        [TestMethod]
        public void EnsureInputFileExists_AppliesConfiguredSyncMode()
        {
            var inputFile = CreateInputFile(FlowBloxInputFileSyncMode.CreateIfNotExists, "managed-v1");
            var project = CreateProject(inputFile);
            var targetPath = FlowBloxInputFileHelper.BuildAbsoluteTargetPath(project.ProjectInputDirectory, inputFile.RelativePath);

            FlowBloxInputFileHelper.EnsureInputFileExists(project, inputFile);
            File.WriteAllText(targetPath, "user-change");
            inputFile.ContentBytes = "managed-v2"u8.ToArray();

            FlowBloxInputFileHelper.EnsureInputFileExists(project, inputFile);
            Assert.AreEqual("user-change", File.ReadAllText(targetPath));

            inputFile.SyncMode = FlowBloxInputFileSyncMode.AlwaysOverwrite;
            FlowBloxInputFileHelper.EnsureInputFileExists(project, inputFile);
            Assert.AreEqual("managed-v2", File.ReadAllText(targetPath));
        }

        [TestMethod]
        public void InputFilePlaceholders_UseCanonicalDoubleColonSyntaxOnly()
        {
            var inputFile = CreateInputFile(FlowBloxInputFileSyncMode.CreateIfNotExists, "content");
            var project = CreateProject(inputFile);
            var targetPath = FlowBloxInputFileHelper.BuildAbsoluteTargetPath(project.ProjectInputDirectory, inputFile.RelativePath);

            var elements = project.GetInputFilePlaceholderElements(inputFile);
            CollectionAssert.AreEquivalent(
                new[] { "$InputFile::Path", "$InputFile::RelativePath" },
                elements.Select(x => x.Placeholder).ToArray());

            var unsupportedSingleColonPlaceholder = "$InputFile" + ":Path";
            var resolved = FlowBloxInputFileHelper.ReplaceInputFilePlaceholders(
                $"$InputFile::Path|$InputFile::RelativePath|{unsupportedSingleColonPlaceholder}",
                project,
                inputFile);

            Assert.AreEqual($"{targetPath}|scripts/model.py|{unsupportedSingleColonPlaceholder}", resolved);
        }

        private static FlowBloxProject CreateProject(FlowBloxInputFile inputFile) => new()
        {
            ProjectName = "Input File Command Test",
            InputFiles = [inputFile]
        };

        private static FlowBloxInputFile CreateInputFile(FlowBloxInputFileSyncMode syncMode, string content) => new()
        {
            RelativePath = "scripts/model.py",
            ContentBytes = System.Text.Encoding.UTF8.GetBytes(content),
            Command = "echo \"$InputFile::Path\"",
            SyncMode = syncMode
        };
    }
}
