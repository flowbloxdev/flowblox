using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using SkiaSharp;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace FlowBloxTest.FlowBlocks.IO;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class PdfOcrFlowBlockTests
{
    [TestMethod]
    public void LegacyTextChannelStillEmitsOneDatasetPerPage()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(600, 200).AddText("First page", 18, new PdfPoint(20, 140), font);
        builder.AddPage(600, 200).AddText("Second page", 18, new PdfPoint(20, 140), font);
        var block = Execute(builder.Build(), false, PdfOcrMode.EmptyPages);
        Assert.HasCount(2, block.GridElementResult.Results);
        Assert.AreEqual("First page", Value(block, 0, block.ResultField).Trim());
        Assert.AreEqual("2", Value(block, 1, block.PageNumberResult));
        Assert.AreEqual(string.Empty, Value(block, 0, block.OcrTextResult));
    }

    [TestMethod]
    public void ScannedPageHasOcrAndSelectedTextInTheSameDataset()
    {
        var block = Execute(CreateScan(false), true, PdfOcrMode.EmptyPages);
        Assert.HasCount(1, block.GridElementResult.Results);
        var ocr = Value(block, 0, block.OcrTextResult);
        StringAssert.Contains(ocr, "7429");
        Assert.AreEqual(ocr, Value(block, 0, block.ResultField));
        Assert.IsTrue(string.IsNullOrWhiteSpace(Value(block, 0, block.PdfTextResult)));
    }

    [TestMethod]
    public void AllPagesModeReadsScanOnPageWithAddedPdfText()
    {
        var block = Execute(CreateScan(true), true, PdfOcrMode.AllPages);
        StringAssert.Contains(Value(block, 0, block.PdfTextResult), "Added PDF text");
        StringAssert.Contains(Value(block, 0, block.OcrTextResult), "7429");
        Assert.HasCount(1, block.GridElementResult.Results);
    }

    [TestMethod]
    public void TextPageFallbackDoesNotRequireOcrLanguageFiles()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(600, 200).AddText("Existing text", 18, new PdfPoint(20, 140), font);
        var block = Execute(builder.Build(), true, PdfOcrMode.EmptyPages, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.AreEqual("Existing text", Value(block, 0, block.ResultField).Trim());
    }

    private static PDF2TextFlowBlock Execute(byte[] bytes, bool useOcr, PdfOcrMode mode, string? dataDirectory = null)
    {
        var project = new FlowBloxProject();
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = project.FlowBloxRegistry.CreateFlowBlockUnregistered<PDF2TextFlowBlock>();
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(block);
        project.FlowBloxRegistry.Register(block);
        var input = new FieldElement(FieldTypes.ByteArray)
        {
            Name = "SyntheticPdf",
            UserField = true,
            UserFieldType = UserFieldTypes.Memory,
            StringValue = Convert.ToBase64String(bytes)
        };
        project.FlowBloxRegistry.Register(input);
        block.DataSource = new MemoryObject
        {
            FileName = "synthetic.pdf",
            Field = input
        };
        block.UseOcr = useOcr;
        block.OcrMode = mode;
        block.OcrDataDirectory = dataDirectory ?? FindLanguageData();
        using var runtime = new TransientRuntime(project);
        var errors = new List<string>();
        runtime.LogMessageCreated += (_, message, level) =>
        {
            if (level.ToString() == "Error")
                errors.Add(message);
        };
        Assert.IsTrue(block.Execute(runtime, null));
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
        return block;
    }

    private static string Value(PDF2TextFlowBlock block, int row, FieldElement field) =>
        block.GridElementResult.Results[row].FieldValueMappings.Single(mapping => mapping.Field == field).Value;

    private static byte[] CreateScan(bool addedText)
    {
        using var bitmap = new SKBitmap(1800, 450);
        using (var canvas = new SKCanvas(bitmap))
        using (var font = new SKFont(SKTypeface.FromFamilyName("Arial"), 85))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText("FLOWBLOX OCR 7429", 80, 250, SKTextAlign.Left, font, paint);
        }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(600, 200);
        page.AddPng(png.ToArray(), new PdfRectangle(0, 0, 600, 150));
        if (addedText)
        {
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText("Added PDF text", 16, new PdfPoint(20, 175), font);
        }
        return builder.Build();
    }

    private static string FindLanguageData()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(directory.FullName, "FlowBlox.Build", "data", "ocr", "tessdata");
            if (Directory.Exists(path))
                return path;
            directory = directory.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "data", "ocr", "tessdata");
    }
}
