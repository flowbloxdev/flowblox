using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.FlowBlocks.Web;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class WebEventLoggingTests
{
    [TestMethod]
    [DataRow("$User::Password")]
    [DataRow("prefix $User::Email / $User::Password suffix")]
    public void PasswordReference_MasksTheEntireResolvedInput(string configuredInput)
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var password = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "Password");
        password.IsPassword = true;
        var block = new WebEventFlowBlock { InputText = configuredInput };

        Assert.AreEqual("<<hidden>>", block.GetInputTextForLogging("prefix secret-value suffix"));
    }

    [TestMethod]
    public void OrdinaryReference_PreservesResolvedInputForDebugging()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "Email");
        var block = new WebEventFlowBlock { InputText = "$User::Email" };

        Assert.AreEqual("demo@example.test", block.GetInputTextForLogging("demo@example.test"));
    }
}
