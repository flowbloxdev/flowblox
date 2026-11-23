using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Services;
using FlowBlox.Core.Util.Json;
using FlowBlox.Core.Models.FlowBlocks.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ComponentSnapshotFilterTests
{
    [TestMethod]
    public async Task FilteredCollection_MatchesFullExportAndOmitsTheFullSnapshot()
    {
        var previous = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        try
        {
            var block = new TableReaderFlowBlock { Name = "Reader" };
            block.MappingEntries.Add(new TableSelectorMappingEntry
            {
                ColumnName = "First",
                Field = new FieldElement { Name = "Value", Source = block, FieldType = new TypeElement { FieldType = FieldTypes.Text } },
                IsOptional = true,
                FallbackValue = "fallback"
            });
            project.FlowBloxRegistry.RegisterFlowBlock(block);
            var handler = new GetComponentSnapshotHandler();
            var args = new JObject { ["kind"] = "flowBlock", ["name"] = block.Name };
            var full = await handler.HandleAsync(args, CancellationToken.None);
            Assert.IsTrue(full.Ok, full.Error);

            args["jsonFilter"] = new JArray("$.MappingEntries");
            var filtered = await handler.HandleAsync(args, CancellationToken.None);

            Assert.IsTrue(filtered.Ok, filtered.Error);
            Assert.IsNull(filtered.Result["snapshot"]);
            var complete = JObject.Parse(JsonConvert.SerializeObject(block, JsonSettings.ComponentSnapshotExport(compact: false)));
            Assert.IsInstanceOfType<JArray>(filtered.Result["matches"]![0]!["value"]);
            Assert.IsTrue(JToken.DeepEquals(complete["MappingEntries"], filtered.Result["matches"]![0]!["value"]));
            Assert.AreEqual("$.MappingEntries", filtered.Result["matches"]![0]!["path"]!.Value<string>());
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previous;
        }
    }

    [TestMethod]
    public async Task FilterIncludesNullPropertiesButRejectsUnknownNames()
    {
        var previous = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        try
        {
            var block = new WebSelectorFlowBlock { Name = "Selector", XPath = null, AttributeName = null };
            project.FlowBloxRegistry.RegisterFlowBlock(block);
            var handler = new GetComponentSnapshotHandler();
            var args = new JObject { ["kind"] = "flowBlock", ["name"] = block.Name };
            var compact = await handler.HandleAsync(args, CancellationToken.None);
            Assert.IsTrue(compact.Ok, compact.Error);
            Assert.IsNull(compact.Result["snapshot"]!["XPath"]);

            args["jsonFilter"] = new JArray("$.XPath", "$.AttributeName");
            var filtered = await handler.HandleAsync(args, CancellationToken.None);
            Assert.IsTrue(filtered.Ok, filtered.Error);
            Assert.AreEqual(2, filtered.Result["matches"]!.Count());
            Assert.AreEqual(JTokenType.Null, filtered.Result["matches"]![0]!["value"]!.Type);
            Assert.AreEqual(JTokenType.Null, filtered.Result["matches"]![1]!["value"]!.Type);

            args["jsonFilter"] = new JArray("$.XPaht");
            var invalid = await handler.HandleAsync(args, CancellationToken.None);
            Assert.IsFalse(invalid.Ok);
            StringAssert.Contains(invalid.Error, "matched no snapshot value");
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previous;
        }
    }

    [TestMethod]
    public async Task FilterCannotExposeAnOmittedPasswordFieldValue()
    {
        var previous = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        try
        {
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Secret");
            field.IsPassword = true;
            field.StringValue = "private-test-value";
            var handler = new GetComponentSnapshotHandler();
            var args = new JObject { ["kind"] = "field", ["name"] = field.FullyQualifiedName };
            var full = await handler.HandleAsync(args, CancellationToken.None);
            Assert.IsTrue(full.Ok, full.Error);
            Assert.IsNull(full.Result["snapshot"]!["StringValue"]);

            args["jsonFilter"] = new JArray("$.StringValue");
            var filtered = await handler.HandleAsync(args, CancellationToken.None);

            Assert.IsFalse(filtered.Ok);
            StringAssert.Contains(filtered.Error, "matched no snapshot value");
            Assert.IsFalse(filtered.Result.ToString().Contains("private-test-value", StringComparison.Ordinal));
            Assert.IsNull(filtered.Result["snapshot"]);
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previous;
        }
    }

    [TestMethod]
    public async Task DeepSeekDsml_PreservesAndExecutesTheJsonPathArray()
    {
        var previous = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        try
        {
            var block = new TableReaderFlowBlock { Name = "Reader" };
            block.SetPropertySelectedFieldName("StartIndex", "$User::Offset");
            project.FlowBloxRegistry.RegisterFlowBlock(block);
            const string output = """
                <||DSML||tool_calls><||DSML||invoke name="FlowBloxAIToolApi-GetComponentSnapshot">
                <||DSML||parameter name="kind" string="true">flowBlock</||DSML||parameter>
                <||DSML||parameter name="name" string="true">Reader</||DSML||parameter>
                <||DSML||parameter name="jsonFilter" string="false">["$.Name","$['MappingEntries','PropertySelectedFieldMap']"]</||DSML||parameter>
                </||DSML||invoke></||DSML||tool_calls>
                """;

            var parsed = new AiResponseInstructionParserService().Parse(output, new DeepSeekAIProvider());
            Assert.IsTrue(parsed.Success);
            var call = parsed.Instruction.ToolCalls.Single();
            Assert.IsInstanceOfType<JArray>(call.Arguments["jsonFilter"]);
            Assert.AreEqual("$['MappingEntries','PropertySelectedFieldMap']", call.Arguments["jsonFilter"]![1]!.Value<string>());
            var normalized = new AiResponseInstructionParserService().Parse(parsed.Instruction.InternalContent);
            var response = await new GetComponentSnapshotHandler().HandleAsync(normalized.Instruction.ToolCalls.Single().Arguments, CancellationToken.None);

            Assert.IsTrue(response.Ok, response.Error);
            Assert.AreEqual(3, response.Result["matches"]!.Count());
            Assert.IsNull(response.Result["snapshot"]);
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previous;
        }
    }
}
