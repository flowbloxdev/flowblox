using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Communication;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ScalarFieldReferenceUpdateTests
{
    [TestMethod]
    public void Update_ResolvesScalarFieldAndUnlinksOrReplacesWithLiteral()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Port");
        field.StringValue = "587";
        var block = new SMTPFlowBlock { Name = "Mail", Port = 25 };
        var args = new JObject
        {
            ["path"] = "/Port/Link",
            ["value"] = new JObject { ["resolveFieldElementByFQName"] = field.FullyQualifiedName }
        };

        var selected = ToolHandlerUtilities.ApplyUpdates(args, block, registry, "FlowBlock", block.Name);
        Assert.IsTrue(selected.Ok, selected.Error);
        Assert.AreEqual(25, block.Port);
        Assert.AreEqual(587, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(block, current => current.Port));

        args["path"] = "/Port/Unlink";
        args.Remove("value");
        Assert.IsTrue(ToolHandlerUtilities.ApplyUpdates(args, block, registry, "FlowBlock", block.Name).Ok);
        Assert.IsNull(block.GetPropertySelectedFieldName(nameof(block.Port)));
        Assert.AreEqual(25, block.Port);

        args["path"] = "/Port/Link";
        args["value"] = field.FullyQualifiedName;
        Assert.IsTrue(ToolHandlerUtilities.ApplyUpdates(args, block, registry, "FlowBlock", block.Name).Ok);
        args["path"] = "/Port";
        args["value"] = 465;
        Assert.IsTrue(ToolHandlerUtilities.ApplyUpdates(args, block, registry, "FlowBlock", block.Name).Ok);
        Assert.AreEqual(465, block.Port);
        Assert.IsNull(block.GetPropertySelectedFieldName(nameof(block.Port)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Update_DirectScalarAssignmentRejectsReferencesAndPreservesExistingSelection(bool useResolver)
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var original = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Original");
        var replacement = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Replacement");
        var block = new SMTPFlowBlock { Name = "Mail", Port = 25 };
        block.SetPropertySelectedField(nameof(block.Port), original);

        var result = ToolHandlerUtilities.ApplyUpdates(new JObject
        {
            ["path"] = "/Port",
            ["value"] = useResolver
                ? new JObject { ["resolveFieldElementByFQName"] = replacement.FullyQualifiedName }
                : new JValue(replacement.FullyQualifiedName)
        }, block, registry, "FlowBlock", block.Name);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(25, block.Port);
        Assert.AreEqual(original.FullyQualifiedName, block.GetPropertySelectedFieldName(nameof(block.Port)));
    }

    [TestMethod]
    public void Update_EnabledBooleanAcceptsFieldWithoutTypeRestriction()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "AcceptCertificate");
        field.StringValue = "true";
        var block = new SMTPFlowBlock { Name = "Mail" };
        var result = ToolHandlerUtilities.ApplyUpdates(new JObject
        {
            ["path"] = "/AcceptInvalidCertificates/Link",
            ["value"] = new JObject { ["resolveFieldElementByFQName"] = field.FullyQualifiedName }
        }, block, registry, "FlowBlock", block.Name);

        Assert.IsTrue(result.Ok, result.Error);
        Assert.IsTrue(FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(block, current => current.AcceptInvalidCertificates));
    }

    [TestMethod]
    public void Update_UnenabledScalarRejectsFieldResolver()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Boolean, "Control");
        field.StringValue = "true";
        var block = new SMTPFlowBlock { Name = "Mail" };

        var result = ToolHandlerUtilities.ApplyUpdates(new JObject
        {
            ["path"] = "/InputIgnoreDuplicates",
            ["value"] = new JObject { ["resolveFieldElementByFQName"] = field.FullyQualifiedName }
        }, block, registry, "FlowBlock", block.Name);

        Assert.IsFalse(result.Ok);
        Assert.IsFalse(block.InputIgnoreDuplicates);
        Assert.IsNull(block.GetPropertySelectedFieldName(nameof(block.InputIgnoreDuplicates)));
    }
}
