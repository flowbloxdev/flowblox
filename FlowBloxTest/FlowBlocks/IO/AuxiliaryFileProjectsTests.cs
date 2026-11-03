using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Test.Runtime;
using Newtonsoft.Json.Linq;
using OfficeOpenXml;

namespace FlowBloxTest.FlowBlocks.IO;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AuxiliaryFileProjectsTests
{
    private string _directory = null!;
    private FlowBloxProject? _previousProject;

    [TestInitialize]
    public void Initialize()
    {
        _previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxFileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        FlowBloxProjectManager.Instance.ActiveProject = _previousProject;
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    [DataRow(false, "*.txt", 1)]
    [DataRow(true, "*.txt", 2)]
    [DataRow(true, "*.txt;!nested/*", 1)]
    [DataRow(true, "", 3)]
    [DataRow(true, "*.missing", 0)]
    public void ListFiles_AggregatesFullPaths(bool recursive, string filter, int count)
    {
        File.WriteAllText(Path.Combine(_directory, "one.txt"), "one");
        File.WriteAllText(Path.Combine(_directory, "two.csv"), "two");
        var nested = Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        File.WriteAllText(Path.Combine(nested.FullName, "three.txt"), "three");

        var content = Run("ListFiles", new()
        {
            ["DirectoryPath"] = _directory,
            ["Recursive"] = recursive.ToString(),
            ["FilterString"] = filter
        }, "Files");

        var paths = content.Split([ '\r', '\n' ], StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(count, paths.Length);
        Assert.IsTrue(paths.All(path => Path.IsPathFullyQualified(path) && File.Exists(path)));
    }

    [TestMethod]
    public void ListFiles_ReturnsFullPathsForRelativeDirectory()
    {
        var path = Path.Combine(_directory, "one.txt");
        File.WriteAllText(path, "one");
        var content = Run("ListFiles", new()
        {
            ["DirectoryPath"] = Path.GetRelativePath(Environment.CurrentDirectory, _directory)
        }, "Files");
        Assert.AreEqual(path, content);
    }

    [TestMethod]
    [DataRow("UTF8")]
    [DataRow("UTF16")]
    [DataRow("ASCII")]
    [DataRow("ISO_8859_1")]
    [DataRow("WINDOWS_1252")]
    public void GetFileContent_ReadsTextWithDynamicEncoding(string encodingName)
    {
        var name = Enum.Parse<DotNetEncodingNames>(encodingName);
        var encoding = FlowBlox.Core.Extensions.DotNetEncodingNamesExtension.ToEncoding(name);
        var expected = encodingName == "ASCII" ? "Hello world" : "Grüße aus Köln";
        var path = Path.Combine(_directory, "text.txt");
        File.WriteAllBytes(path, encoding.GetBytes(expected));

        Assert.AreEqual(expected, RunContent(path, "Text", encodingName));
    }

    [TestMethod]
    public void GetFileContent_UsesPersistedUtf8Default()
    {
        var path = Path.Combine(_directory, "text.txt");
        File.WriteAllText(path, "Grüße", new UTF8Encoding(false));
        Assert.AreEqual("Grüße", RunContent(path, "Text"));
    }

    [TestMethod]
    public void GetFileContent_JoinsAllDocxPages()
    {
        var path = Path.Combine(_directory, "document.docx");
        using (var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("First page"))),
                new Paragraph(new Run(new Break { Type = BreakValues.Page })),
                new Paragraph(new Run(new Text("Second page")))));
        }

        var content = RunContent(path, "Docx");
        StringAssert.Contains(content, "First page");
        StringAssert.Contains(content, "Second page");
    }

    [TestMethod]
    public void GetFileContent_ConvertsXlsxToCsv()
    {
        ExcelPackage.License.SetNonCommercialPersonal("FlowBlox tests");
        var path = Path.Combine(_directory, "table.xlsx");
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Data");
            sheet.Cells[1, 1].Value = "Name";
            sheet.Cells[1, 2].Value = "Note";
            sheet.Cells[2, 1].Value = "Ada";
            sheet.Cells[2, 2].Value = "a,b";
            package.SaveAs(new FileInfo(path));
        }

        var content = RunContent(path, "Xlsx");
        StringAssert.Contains(content, "Name,Note");
        StringAssert.Contains(content, "Ada,\"a,b\"");
    }

    [TestMethod]
    public void GetFileContent_JoinsAllPdfPages()
    {
        var path = Path.Combine(_directory, "document.pdf");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            PdfStream("First page"),
            PdfStream("Second page")
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);

        var content = RunContent(path, "Pdf");
        StringAssert.Contains(content, "First page");
        StringAssert.Contains(content, "Second page");
    }

    private static string PdfStream(string text)
    {
        var stream = $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET\n";
        return $"<< /Length {stream.Length} >>\nstream\n{stream}endstream";
    }

    [TestMethod]
    public async Task Discovery_ReturnsInterfacesAndOnlyExistingValueLists()
    {
        var response = await new DefaultToolApi().ExecuteAsync(new ToolRequest
        {
            ToolName = "GetAuxiliaryProjects",
            Arguments = new JObject
            {
                ["mode"] = "distributedData",
                ["filter"] = "ListFiles GetFileContent"
            }
        }, CancellationToken.None);
        Assert.IsTrue(response.Ok, response.Error);
        var projects = response.Result["projects"]!.Children<JObject>().ToList();
        var content = projects.Single(x => x.Value<string>("projectName") == "GetFileContent");
        CollectionAssert.AreEquivalent(new[] { "FilePath", "InputFileType", "Encoding" }, content["inputFields"]!.Values<string>().ToArray());
        CollectionAssert.AreEqual(new[] { "FileContent" }, content["outputFields"]!.Values<string>().ToArray());
        CollectionAssert.AreEqual(new[] { "Pdf", "Xlsx", "Docx", "Text" }, content["inputFieldListOfValues"]!["InputFileType"]!.Values<string>().ToArray());
        Assert.AreEqual(5, content["inputFieldListOfValues"]!["Encoding"]!.Count());
        Assert.IsNull(projects.Single(x => x.Value<string>("projectName") == "ListFiles")["inputFieldListOfValues"]);
    }

    private static string RunContent(string path, string type, string? encoding = null)
    {
        var inputs = new Dictionary<string, string> { ["FilePath"] = path, ["InputFileType"] = type };
        if (encoding != null)
            inputs["Encoding"] = encoding;
        return Run("GetFileContent", inputs, "FileContent");
    }

    private static string Run(string name, Dictionary<string, string> inputs, string output)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../FlowBlox.Build/data/auxiliary_project/{name}.fbprj"));
        var project = FlowBloxProject.FromFile(path);
        FlowBloxProjectManager.Instance.ActiveProject = project;
        foreach (var input in inputs)
            project.UserFields.Single(x => x.Name == input.Key).StringValue = input.Value;
        var runtime = new FlowBloxUnitTestRuntime(project);
        runtime.LogMessageCreated += (_, message, _) => Console.WriteLine(message);
        runtime.Execute();
        var datasets = runtime.GetOutputDatasets("Output");
        Assert.AreEqual(1, datasets.Count, $"Expected one {name} output dataset.");
        return datasets[0].Values[output]?.ToString() ?? string.Empty;
    }
}
