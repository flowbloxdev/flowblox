using FlowBlox.Core.Constants;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [DoNotParallelize]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class GetOutputFileContentHandlerTests
    {
        private string _temporaryOutputRoot = null!;
        private string _previousOutputRoot = null!;
        private FlowBloxProject? _previousProject;

        [TestInitialize]
        public void Initialize()
        {
            _temporaryOutputRoot = Path.Combine(Path.GetTempPath(), "FlowBloxAiOutputFileTests", Guid.NewGuid().ToString("N"));
            var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsOutputDir)
                ?? throw new AssertFailedException("Paths.OutputDir option is missing.");
            _previousOutputRoot = option.Value;
            option.Value = _temporaryOutputRoot;
            _previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        }

        [TestCleanup]
        public void Cleanup()
        {
            FlowBloxProjectManager.Instance.ActiveProject = _previousProject;

            var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsOutputDir);
            if (option != null)
                option.Value = _previousOutputRoot;

            if (Directory.Exists(_temporaryOutputRoot))
                Directory.Delete(_temporaryOutputRoot, recursive: true);
        }

        [TestMethod]
        public async Task GetOutputFileContent_ReadsTextFileBelowProjectOutputDirectory()
        {
            var project = new FlowBloxProject { ProjectName = "Output content test" };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var outputFile = Path.Combine(project.ProjectOutputDirectory, "reports", "result.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            await File.WriteAllBytesAsync(outputFile, Encoding.UTF8.GetBytes("Id;Name\n1;Test"));

            var response = await Execute("reports/result.csv");

            Assert.IsTrue(response.Ok, response.Error);
            Assert.AreEqual("reports/result.csv", response.Result.Value<string>("key"));
            Assert.AreEqual("text/csv", response.Result.Value<string>("mimeType"));
            Assert.AreEqual("Id;Name\n1;Test", response.Result.Value<string>("textContent"));
        }

        [TestMethod]
        public async Task GetOutputFileContent_RejectsPathTraversal()
        {
            var project = new FlowBloxProject { ProjectName = "Output containment test" };
            FlowBloxProjectManager.Instance.ActiveProject = project;

            var response = await Execute("../outside.txt");

            Assert.IsFalse(response.Ok);
            StringAssert.Contains(response.Error, "must not contain");
        }

        [TestMethod]
        public async Task GetOutputFileContent_ConvertsDocxToPageOrientedText()
        {
            var project = new FlowBloxProject { ProjectName = "DOCX output content test" };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var outputFile = Path.Combine(project.ProjectOutputDirectory, "report.docx");
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            await File.WriteAllBytesAsync(outputFile, CreateDocx());

            var response = await Execute("report.docx");

            Assert.IsTrue(response.Ok, response.Error);
            Assert.AreEqual("Docx2TextConverter", response.Result.Value<string>("converter"));
            var content = response.Result.Value<string>("textContent") ?? string.Empty;
            StringAssert.Contains(content, "<page number=\"1\">");
            StringAssert.Contains(content, "<page number=\"2\">");
            StringAssert.Contains(content, "<header>");
            StringAssert.Contains(content, "Header text");
            StringAssert.Contains(content, "First page");
            StringAssert.Contains(content, "Second page");
            StringAssert.Contains(content, "Footer text");
        }

        [TestMethod]
        public async Task GetOutputFileContent_WhenTextConversionDisabled_ReturnsBase64()
        {
            var project = new FlowBloxProject { ProjectName = "Base64 output content test" };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var outputFile = Path.Combine(project.ProjectOutputDirectory, "result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            var bytes = Encoding.UTF8.GetBytes("test content");
            await File.WriteAllBytesAsync(outputFile, bytes);

            var response = await Execute("result.txt", textConversion: false);

            Assert.IsTrue(response.Ok, response.Error);
            Assert.AreEqual(string.Empty, response.Result.Value<string>("textContent"));
            Assert.AreEqual(Convert.ToBase64String(bytes), response.Result.Value<string>("base64Content"));
            Assert.AreEqual("base64", response.Result.Value<string>("contentEncoding"));
        }

        private static byte[] CreateDocx()
        {
            using var stream = new MemoryStream();
            using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
            {
                var mainPart = document.AddMainDocumentPart();
                var headerPart = mainPart.AddNewPart<HeaderPart>();
                headerPart.Header = new Header(new Paragraph(new Run(new Text("Header text"))));
                var footerPart = mainPart.AddNewPart<FooterPart>();
                footerPart.Footer = new Footer(new Paragraph(new Run(new Text("Footer text"))));

                var body = new Body(
                    new Paragraph(new Run(new Text("First page"))),
                    new Paragraph(new Run(new Break { Type = BreakValues.Page })),
                    new Paragraph(new Run(new Text("Second page"))),
                    new SectionProperties(
                        new HeaderReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(headerPart) },
                        new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footerPart) }));
                mainPart.Document = new Document(body);
            }

            return stream.ToArray();
        }

        private static Task<ToolResponse> Execute(string key, bool? textConversion = null) =>
            new DefaultToolApi().ExecuteAsync(
                new ToolRequest
                {
                    ToolName = "GetOutputFileContent",
                    Arguments = new JObject
                    {
                        ["key"] = key,
                        ["textConversion"] = textConversion.HasValue
                            ? new JValue(textConversion.Value)
                            : JValue.CreateNull()
                    }
                },
                CancellationToken.None);
    }
}
