using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.Components.Modifier;
using FlowBlox.Core.Models.FlowBlocks.AIRemote;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Enums;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Format;
using FlowBlox.Core.Models.FlowBlocks.Generation;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.FlowBlocks.Xml;
using FlowBlox.Core.Models.Generators;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBloxTest.FlowBlocks;

namespace FlowBloxTest.Runtime;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class BusinessPropertyFieldReferenceTests : FlowBloxTestsBase
{
    private IDisposable _registryScope = null!;
    private FlowBloxProject _project = null!;

    [TestInitialize]
    public void Initialize()
    {
        _project = new FlowBloxProject();
        _registryScope = FlowBloxRegistryProvider.BeginScopedRegistry(_project.FlowBloxRegistry);
        CreateFlowBlock<StartFlowBlock>();
    }

    [TestCleanup]
    public void Cleanup() => _registryScope.Dispose();

    [TestMethod]
    public void Counter_ResolvesBoundsAndFormatWithoutChangingConfiguration()
    {
        var start = AddField("Start", "2");
        var end = AddField("End", "3");
        var format = AddField("Format", "D2");
        var block = CreateFlowBlock<CounterFlowBlock>();
        block.StartValue = start.FullyQualifiedName;
        block.FinalValue = end.FullyQualifiedName;
        block.Format = format.FullyQualifiedName;
        var context = new ValidationContext(block) { MemberName = nameof(block.StartValue) };

        Assert.AreEqual(ValidationResult.Success, CounterFlowBlock.ValidateStartValue(block.StartValue, context));
        Assert.IsTrue(block.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "02", "03" }, Results(block));
        Assert.AreEqual(start.FullyQualifiedName, block.StartValue);
        Assert.AreEqual(format.FullyQualifiedName, block.Format);

        start.StringValue = "3";
        Assert.IsTrue(block.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "03" }, Results(block));
    }

    [TestMethod]
    public void AlphanumericCounter_ResolvesAlphabetAndBounds()
    {
        var alphabet = AddField("Alphabet", "ab");
        var format = AddField("Format", "X");
        var end = AddField("End", "B");
        var block = CreateFlowBlock<CounterFlowBlock>();
        block.CounterType = CounterFlowBlock.CounterTypes.Alphanumeric;
        block.AlphaRange = alphabet.FullyQualifiedName;
        block.Format = format.FullyQualifiedName;
        block.FinalValue = end.FullyQualifiedName;

        Assert.IsTrue(block.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "A", "B" }, Results(block));
        Assert.AreEqual(alphabet.FullyQualifiedName, block.AlphaRange);
    }

    [TestMethod]
    public void Mapping_UsesResolvedKeysAndRegexDuringValidationAndExecution()
    {
        var key = AddField("Key", "ticket");
        var regex = AddField("Regex", "^item-[0-9]+$");
        var input = AddField("Input", "ticket");
        var block = CreateFlowBlock<MappingFlowBlock>();
        block.InputField = input;
        block.MappingEntries.Add(new MappingFlowBlockEntry { Key = key.FullyQualifiedName, Value = "key match" });
        var regexEntry = new MappingFlowBlockEntry { RegularExpression = regex.FullyQualifiedName, Value = "regex match" };
        block.MappingEntries.Add(regexEntry);

        Assert.HasCount(0, regexEntry.Validate(new ValidationContext(regexEntry)).ToList());
        Assert.IsTrue(block.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "key match" }, Results(block));

        input.StringValue = "item-42";
        Assert.IsTrue(block.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "regex match" }, Results(block));
        Assert.AreEqual(regex.FullyQualifiedName, regexEntry.RegularExpression);

        regex.StringValue = "[";
        Assert.HasCount(1, regexEntry.Validate(new ValidationContext(regexEntry)).ToList());
    }

    [TestMethod]
    public void SplitAndJoin_UseCurrentDelimiterAndPreserveSpecialSeparators()
    {
        var delimiter = AddField("Delimiter", "|");
        var input = AddField("Input", "one|two");
        var split = CreateFlowBlock<SplitFlowBlock>();
        split.InputField = input;
        split.Separators.Clear();
        split.Separators.Add(new SplitSeparatorDefinition { Separator = delimiter.FullyQualifiedName });

        Assert.IsTrue(split.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "one", "two" }, Results(split));

        var join = CreateFlowBlock<JoinFlowBlock>();
        join.Separator = delimiter.FullyQualifiedName;
        join.JoinedParameters.Add(AddField("First", "one"));
        join.JoinedParameters.Add(AddField("Second", "two"));
        Assert.IsTrue(join.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "one|two" }, Results(join));

        delimiter.StringValue = ";";
        Assert.IsTrue(join.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "one;two" }, Results(join));
        Assert.AreEqual(delimiter.FullyQualifiedName, join.Separator);
        Assert.AreEqual("\t", new SplitSeparatorDefinition { SpecialSeparator = SpecialSeparator.Tab }.ResolveSeparator());
    }

    [TestMethod]
    public void PatternSelection_ReturnsResolvedDelimitersWithSelectedText()
    {
        var start = AddField("Start", "<item>");
        var end = AddField("End", "</item>");
        var block = CreateFlowBlock<StartEndPatternSelectorFlowBlock>();
        block.InputField = AddField("Input", "before<item>value</item>after");
        var pattern = new StartEndPattern
        {
            StartPattern = start.FullyQualifiedName,
            EndPattern = end.FullyQualifiedName,
            ReturnOptions = StartEndPatternReturnOptions.StartAndEndPattern
        };
        block.StartEndPatterns.Add(pattern);

        Assert.IsTrue(block.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "<item>value</item>" }, Results(block));
        Assert.AreEqual(start.FullyQualifiedName, pattern.StartPattern);
        Assert.AreEqual(end.FullyQualifiedName, pattern.EndPattern);
    }

    [TestMethod]
    public void RegexSelection_UsesResolvedGroupAndModifierSeparator()
    {
        var group = AddField("Group", "number");
        var separator = AddField("Separator", "|");
        var block = CreateFlowBlock<RegexSelectorFlowBlock>();
        block.InputField = AddField("Input", "item-12 item-34");
        block.RegularExpression = "item-(?<number>[0-9]+)";
        block.Group = group.FullyQualifiedName;

        Assert.IsTrue(block.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "12", "34" }, Results(block));
        var modifier = new RegexMatchModifier { Search = "[0-9]+", Separator = separator.FullyQualifiedName };
        Assert.AreEqual("12|34", modifier.Modify(new TransientRuntime(_project), block.InputField.StringValue));
        Assert.AreEqual(group.FullyQualifiedName, block.Group);
    }

    [TestMethod]
    public void XmlWriter_ResolvesParentAndNodeNameForCreateAndUpdate()
    {
        var xpath = AddField("XPath", "/root/parent");
        var nodeName = AddField("Node", "entry");
        var document = CreateFlowBlock<XmlDocumentFlowBlock>();
        document.XmlContent = "<root><parent /></root>";
        var writer = CreateFlowBlock<XmlDocumentNodeWriterFlowBlock>();
        writer.AssociatedXmlDocument = document;
        writer.XPath = xpath.FullyQualifiedName;
        writer.NodeName = nodeName.FullyQualifiedName;
        writer.Assignments.Add(new XmlAssignment { XPath = "@id", Value = "first" });
        var runtime = new TransientRuntime(_project);

        Assert.IsTrue(document.Execute(runtime, null!));
        Assert.IsTrue(writer.Execute(runtime, null!));
        writer.Assignments[0].Value = "second";
        Assert.IsTrue(writer.Execute(runtime, null!));
        Assert.AreEqual(1, document.InternalXmlDocument.SelectNodes("/root/parent/entry")!.Count);
        Assert.AreEqual("second", document.InternalXmlDocument.SelectSingleNode("/root/parent/entry/@id")!.Value);
        Assert.AreEqual(xpath.FullyQualifiedName, writer.XPath);
        Assert.AreEqual(nodeName.FullyQualifiedName, writer.NodeName);
    }

    [TestMethod]
    public void CsvTable_ResolvesDelimiterForReadingAndWriting()
    {
        var delimiter = AddField("Delimiter", "|");
        var csv = AddField("Csv", "Name|Count\r\nTicket|2\r\n");
        var encoding = AddField("Encoding", "Unicode");
        var header = AddField("Header", "true");
        var memory = new MemoryObject { Field = csv, FileName = "data.csv", EncodingName = DotNetEncodingNames.ASCII };
        memory.SetPropertySelectedField(nameof(memory.EncodingName), encoding);
        var table = new CsvTable
        {
            Separator = delimiter.FullyQualifiedName,
            EncodingName = DotNetEncodingNames.UTF8,
            FirstRowHeader = false,
            DataSource = memory
        };
        table.SetPropertySelectedField(nameof(table.EncodingName), encoding);
        table.SetPropertySelectedField(nameof(table.FirstRowHeader), header);

        var data = table.Read();
        Assert.AreEqual(2, data.Columns.Count);
        Assert.AreEqual("Ticket", data.Rows[0]["Name"]);
        delimiter.StringValue = ";";
        table.Write(data);
        StringAssert.Contains(csv.StringValue, "Name;Count");
        Assert.AreEqual(delimiter.FullyQualifiedName, table.Separator);
        Assert.AreEqual(DotNetEncodingNames.UTF8, table.EncodingName);
        Assert.AreEqual(DotNetEncodingNames.ASCII, memory.EncodingName);
        Assert.IsFalse(table.FirstRowHeader);
    }

    [TestMethod]
    public void Format_ValidatesAndExecutesTheResolvedExpression()
    {
        var expression = AddField("Format", "Ticket {0}");
        var block = CreateFlowBlock<FormatFlowBlock>();
        block.FormatExpression = expression.FullyQualifiedName;
        block.FormatParameterDefinitions.Add(new FormatParameterDefinition { Field = AddField("Id", "42") });
        var context = new ValidationContext(block) { MemberName = nameof(block.FormatExpression) };

        Assert.AreEqual(ValidationResult.Success, FormatFlowBlock.ValidateFormatExpression(block.FormatExpression, context));
        Assert.IsTrue(block.Execute(new TransientRuntime(_project), null!));
        CollectionAssert.AreEqual(new[] { "Ticket 42" }, Results(block));
        Assert.AreEqual(expression.FullyQualifiedName, block.FormatExpression);
    }

    [TestMethod]
    public void AiPrompt_ResolvesSystemInstructionAndPreservesTemplate()
    {
        var instruction = AddField("Instruction", "Return a ticket summary.");
        var provider = CreateManagedObject<FieldReferenceCapturingAIProvider>();
        var block = CreateFlowBlock<AIPromptFlowBlock>();
        block.Provider = provider;
        block.PromptTemplate = "Summarize";
        block.SystemInstruction = instruction.FullyQualifiedName;

        Assert.IsTrue(block.Execute(new TransientRuntime(_project), null!));
        Assert.AreEqual("Return a ticket summary.", provider.LastRequest.SystemMessages.Single().Content);
        Assert.AreEqual(instruction.FullyQualifiedName, block.SystemInstruction);
    }

    [TestMethod]
    public void GenerationStrategy_ResolvesNullableScalarsAndProviderSettingsOutsideInvocation()
    {
        var temperature = AddField("Temperature", "0.25");
        var tokens = AddField("Tokens", "128");
        var timeout = AddField("Timeout", "30");
        var reasoning = AddField("Reasoning", "High");
        var provider = CreateManagedObject<FieldReferenceCapturingAIProvider>();
        provider.SetPropertySelectedField(nameof(provider.TimeoutSeconds), timeout);
        provider.SetPropertySelectedField(nameof(provider.ReasoningEffort), reasoning);
        var strategy = new AIPropertyValueGenerationStrategy(CreateFlowBlock<MappingFlowBlock>())
        {
            Provider = provider,
            PromptTemplate = "Generate configuration"
        };
        strategy.SetPropertySelectedField(nameof(strategy.Temperature), temperature);
        strategy.SetPropertySelectedField(nameof(strategy.MaxTokens), tokens);

        Assert.AreEqual("generated", strategy.Execute(new TransientRuntime(_project), new()));
        Assert.AreEqual(0.25, provider.LastRequest.Temperature);
        Assert.AreEqual(128, provider.LastRequest.MaxTokens);
        Assert.AreEqual(30, provider.ResolvedTimeout);
        Assert.AreEqual(AIReasoningEffort.High, provider.ResolvedReasoningEffort);
        Assert.IsNull(strategy.Temperature);
        Assert.IsNull(strategy.MaxTokens);
        Assert.AreEqual(AIReasoningEffort.Medium, provider.ReasoningEffort);
    }

    [TestMethod]
    public void MemoryObject_UsesResolvedFileNameWhenMaterializingContent()
    {
        var name = AddField("FileName", "ticket.txt");
        var memory = new MemoryObject
        {
            Field = AddField("Content", "ticket content"),
            FileName = name.FullyQualifiedName,
            EncodingName = DotNetEncodingNames.UTF8
        };

        try
        {
            Assert.IsTrue(memory.CanRead());
            memory.CreateTemporaryFile();
            Assert.AreEqual("ticket.txt", Path.GetFileName(memory.TemporaryFilePath));
            Assert.AreEqual("ticket content", File.ReadAllText(memory.TemporaryFilePath));
            Assert.AreEqual(name.FullyQualifiedName, memory.FileName);
        }
        finally
        {
            if (memory.TemporaryFilePath != null)
            {
                File.Delete(memory.TemporaryFilePath);
                Directory.Delete(Path.GetDirectoryName(memory.TemporaryFilePath)!);
            }
        }
    }

    private FieldElement AddField(string name, string value)
    {
        var field = CreateUserField(name);
        field.StringValue = value;
        return field;
    }

    private static string[] Results(BaseSingleResultFlowBlock block) => block.GridElementResult.Results
        .SelectMany(result => result.FieldValueMappings)
        .Select(mapping => mapping.Value)
        .ToArray();
}
