using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Vault;
using FlowBlox.Test.Runtime;
using FlowBloxTest.FlowBlocks;
using FlowBloxTest.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.Xml;

namespace FlowBloxTest.Util;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class VaultTests : FlowBloxTestsBase
{
    private const string Server = "https://local-db.example.invalid:1433";
    private const string Password = "vault-only-\"password\\42";
    private OptionElement _previousOption;
    private FlowBloxProject _previousProject;
    private FlowBloxProject _project;

    [TestInitialize]
    public void Initialize()
    {
        var options = FlowBloxOptions.GetOptionInstance();
        _previousOption = options.GetOption(OptionsConstants.VaultValues);
        options.OptionCollection[OptionsConstants.VaultValues] = new OptionElement(
            OptionsConstants.VaultValues, string.Empty, string.Empty, OptionElement.OptionType.Password);
        SetEntries(
            new VaultEntry { Name = "Server", Value = Server },
            new VaultEntry { Name = "Password", Value = Password, IsPassword = true });
        _previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        _project = new FlowBloxProject { ProjectName = "Vault integration" };
        FlowBloxProjectManager.Instance.ActiveProject = _project;
        CreateFlowBlock<StartFlowBlock>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.VaultValues] = _previousOption;
        FlowBloxProjectManager.Instance.ActiveProject = _previousProject;
        ToolHandlerUtilities.SetAssistantConfigurationProvider(null);
    }

    private static void SetEntries(params VaultEntry[] entries) =>
        FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.VaultValues).PersistentValue = JsonConvert.SerializeObject(entries);

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TextResolution_RespectsExplicitVaultHandling(bool resolveVaultReferences)
    {
        var result = FlowBloxFieldHelper.ReplaceFieldsInString("connect=$Vault::Server", resolveVaultReferences);
        Assert.AreEqual("connect=" + (resolveVaultReferences ? Server : "$Vault::Server"), result);
    }

    [TestMethod]
    [DataRow(UserFieldTypes.Input, true)]
    [DataRow(UserFieldTypes.Input, false)]
    [DataRow(UserFieldTypes.Memory, true)]
    [DataRow(UserFieldTypes.Memory, false)]
    public void FieldResolution_ResolvesIntroducedVaultReferencesWithoutChangingConfiguration(UserFieldTypes type, bool resolveVaultReferences)
    {
        var field = Registry.CreateUserField(type, FieldTypes.Text, "ServerBinding");
        field.StringValue = "$Vault::Server";

        var result = FlowBloxFieldHelper.ReplaceFieldsInString("connect=" + field.FullyQualifiedName, resolveVaultReferences);

        Assert.AreEqual("connect=" + (resolveVaultReferences ? Server : "$Vault::Server"), result);
        Assert.AreEqual("$Vault::Server", field.StringValue);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ProjectResolution_ResolvesVaultReferencesAfterProjectSubstitution(bool resolveVaultReferences)
    {
        _project.ProjectName = "$Vault::Server";

        var result = FlowBloxFieldHelper.ReplaceFieldsInString("connect=$Project::Name", resolveVaultReferences);

        Assert.AreEqual("connect=" + (resolveVaultReferences ? Server : "$Vault::Server"), result);
    }

    [TestMethod]
    [DataRow(UserFieldTypes.Input)]
    [DataRow(UserFieldTypes.Memory)]
    public void TextFieldValue_ResolvesVaultOnlyForInputWithoutReplacingItsReference(UserFieldTypes type)
    {
        var field = Registry.CreateUserField(type, FieldTypes.Text, "PasswordBinding");
        field.StringValue = "$Vault::Password";

        Assert.AreEqual(type == UserFieldTypes.Input ? Password : "$Vault::Password", field.Value);
        Assert.AreEqual("$Vault::Password", field.StringValue);
    }

    [TestMethod]
    public void TextResolution_PreservesUnknownVaultReferences()
    {
        Assert.AreEqual("$Vault::Missing", FlowBloxFieldHelper.ReplaceFieldsInString("$Vault::Missing", resolveVaultReferences: true));
    }

    [TestMethod]
    [DataRow("", "Empty")]
    [DataRow("   ", "Empty")]
    public void Startup_RejectsMissingEffectiveRequiredVaultValue(string value, string entryName)
    {
        if (value != null)
            SetEntries(new VaultEntry { Name = "Empty", Value = value });
        var input = Registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "RequiredInput");
        input.StringValue = "$Vault::" + entryName;
        input.IsRequiredAtRuntimeStartup = true;
        var runtime = new FlowBloxUnitTestRuntime(_project);

        var exception = Assert.ThrowsExactly<ValidationException>(runtime.ValidateUserFieldsAtRuntimeStartup);

        StringAssert.Contains(exception.Message, input.FullyQualifiedName);
        Assert.IsFalse(exception.Message.Contains(Password, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Startup_ResolvesTypedInputWithoutReplacingItsPersistentReference()
    {
        SetEntries(new VaultEntry { Name = "Port", Value = "1433" });
        var input = Registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Integer, "Port");
        input.StringValue = "$Vault::Port";
        input.IsRequiredAtRuntimeStartup = true;
        var runtime = new FlowBloxUnitTestRuntime(_project);

        runtime.ValidateUserFieldsAtRuntimeStartup();

        Assert.AreEqual(1433, input.Value);
        input.ReleaseRuntimeValue(runtime);
        Assert.AreEqual("$Vault::Port", input.StringValue);
    }

    [TestMethod]
    public void RequiredFields_CheckResolvedValueInsteadOfNonEmptyReference()
    {
        var input = Registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "RequiredInput");
        input.StringValue = "$Vault::Missing";
        var block = CreateFlowBlock<TextBuilderAppendFlowBlock>();
        block.RequiredFields.Add(input);

        Assert.IsTrue(block.ValidateRequirements(out _));
        SetEntries(new VaultEntry { Name = "Missing", Value = "" });
        Assert.IsFalse(block.ValidateRequirements(out var missing));
        Assert.IsTrue(missing.Any(message => message.Contains(input.FullyQualifiedName, StringComparison.Ordinal)));
        SetEntries(new VaultEntry { Name = "Missing", Value = "Available" });
        Assert.IsTrue(block.ValidateRequirements(out _));
    }

    [TestMethod]
    [DataRow("$ABC::53")]
    [DataRow("$Vault::Missing")]
    public void Startup_AllowsPossibleUnresolvedReferencesAsRequiredValues(string value)
    {
        var input = Registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "RequiredInput");
        input.StringValue = value;
        input.IsRequiredAtRuntimeStartup = true;
        var runtime = new FlowBloxUnitTestRuntime(_project);

        runtime.ValidateUserFieldsAtRuntimeStartup();

        Assert.AreEqual(value, input.StringValue);
    }

    [TestMethod]
    public void RuntimeWarnings_InspectLiveOptionalFieldsOnEachInvocation()
    {
        var input = Registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "OptionalInput");
        var block = CreateFlowBlock<UnresolvedFieldProbeFlowBlock>();
        block.Text = input.FullyQualifiedName;
        var runtime = new FlowBloxUnitTestRuntime(_project);
        var warnings = new List<string>();
        runtime.LogMessageCreated += (_, message, level) =>
        {
            if (level == FlowBloxLogLevel.Warning)
                warnings.Add(message);
        };

        Assert.IsTrue(block.Execute(runtime, null));
        Assert.IsEmpty(warnings);
        input.StringValue = "$Vault::Missing";
        Assert.IsTrue(block.Execute(runtime, null));
        Assert.IsTrue(block.Execute(runtime, null));
        Assert.HasCount(2, warnings);
        StringAssert.Contains(warnings.Last(), "$Vault::Missing");

        var nextRuntime = new FlowBloxUnitTestRuntime(_project);
        nextRuntime.LogMessageCreated += (_, message, level) =>
        {
            if (level == FlowBloxLogLevel.Warning)
                warnings.Add(message);
        };
        Assert.IsTrue(block.Execute(nextRuntime, null));
        Assert.HasCount(3, warnings);
        input.StringValue = "$Unknown::Field";
        Assert.IsTrue(block.Execute(nextRuntime, null));
        Assert.HasCount(4, warnings);
        StringAssert.Contains(warnings.Last(), "$Unknown::Field");
    }

    [TestMethod]
    public void SkillInputParameters_PreserveOpaqueReferencesUntilRunnerStartup()
    {
        var mainInput = Registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "MainPassword");
        mainInput.StringValue = "$Vault::Password";
        var memory = Registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "MainMemory");
        memory.StringValue = "DEMO-42";
        var skill = new FlowBloxProject();
        skill.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "Password");
        skill.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "Ticket");
        skill.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "ProjectPath");

        skill.UserFields = skill.FlowBloxRegistry.GetUserFields().ToList();

        var inputs = ProjectExecutionToolUtilities.ResolveInputParameters(new JObject
        {
            ["Password"] = mainInput.FullyQualifiedName,
            ["Ticket"] = memory.FullyQualifiedName,
            ["ProjectPath"] = "$Project::InputDirectory"
        }, skill);

        Assert.AreEqual("$Vault::Password", inputs["Password"]);
        Assert.AreEqual("DEMO-42", inputs["Ticket"]);
        Assert.IsFalse(inputs["ProjectPath"].Contains("$Project::", StringComparison.Ordinal));
        Assert.IsFalse(JsonConvert.SerializeObject(inputs).Contains(Password, StringComparison.Ordinal));
        var direct = ProjectExecutionToolUtilities.ResolveInputParameters(new JObject { ["Password"] = "$Vault::Password" }, skill);
        Assert.AreEqual("$Vault::Password", direct["Password"]);

        FlowBloxProjectManager.Instance.ActiveProject = skill;
        CreateFlowBlock<StartFlowBlock>();
        var target = skill.UserFields.Single(field => field.Name == "Password");
        target.StringValue = inputs["Password"];
        var runtime = new FlowBloxUnitTestRuntime(skill);
        runtime.ValidateUserFieldsAtRuntimeStartup();
        Assert.AreEqual(Password, target.Value);
        target.ReleaseRuntimeValue(runtime);
        Assert.AreEqual("$Vault::Password", target.StringValue);
    }

    [TestMethod]
    public async Task Discovery_ReturnsNamesAndReferencesWithoutValuesOrVaultOptionAccess()
    {
        var api = new DefaultToolApi(() => new AssistantConfiguration { OptionsAccessLevel = AssistantOptionsAccessLevel.ReadWrite });
        var response = await api.ExecuteAsync(new ToolRequest
        {
            ToolName = "GetPlaceholders",
            Arguments = new JObject()
        }, CancellationToken.None);

        Assert.IsTrue(response.Ok, response.Error);
        var references = (JArray)response.Result["vaultPlaceholders"];
        Assert.HasCount(2, references);
        Assert.IsTrue(references.Any(item => item.Value<string>("placeholder") == "$Vault::Password"));
        var content = JsonConvert.SerializeObject(response);
        Assert.IsFalse(content.Contains(JsonConvert.ToString(Password)[1..^1], StringComparison.Ordinal));
        Assert.IsFalse(content.Contains(Server, StringComparison.Ordinal));

        var options = await api.ExecuteAsync(new ToolRequest
        {
            ToolName = "SearchOptions",
            Arguments = new JObject { ["searchForNames"] = "Vault" }
        }, CancellationToken.None);
        Assert.IsTrue(options.Ok, options.Error);
        Assert.IsEmpty((JArray)options.Result["options"]);
        var write = await api.ExecuteAsync(new ToolRequest
        {
            ToolName = "SetOptionValue",
            Arguments = new JObject { ["key"] = OptionsConstants.VaultValues, ["stringValue"] = "[]" }
        }, CancellationToken.None);
        Assert.IsFalse(write.Ok);
        Assert.AreEqual(Password, FlowBloxVault.Resolve("$Vault::Password"));
    }

    [TestMethod]
    public void PasswordFlag_ProtectsFieldChangesWhileUrlsRemainReadable()
    {
        var secret = Registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "ProtectedPassword");
        secret.IsPassword = true;
        var server = Registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "VisibleServer");
        var runtime = new FlowBloxUnitTestRuntime(_project);
        var messages = new List<string>();
        runtime.LogMessageCreated += (_, message, _) => messages.Add(message);

        secret.SetValue(runtime, Password, runtimeBound: true);
        server.SetValue(runtime, Server, runtimeBound: true);

        Assert.IsTrue(messages.All(message => !message.Contains(Password, StringComparison.Ordinal)));
        Assert.IsTrue(messages.Any(message => message.Contains(Server, StringComparison.Ordinal)));
        Assert.AreEqual(GlobalConstants.HiddenSensitiveValue, secret.GetExternalValue(Password));
        Assert.AreEqual(Server, server.GetExternalValue(Server));
        secret.ReleaseRuntimeValue(runtime);
        server.ReleaseRuntimeValue(runtime);
    }

    [TestMethod]
    public void Save_PersistsEncryptedJsonAndDoesNotShareMutableEditorValuesWithResolver()
    {
        var previousDirectory = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var directory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "TestResults", "vault", Guid.NewGuid().ToString("N")));
        try
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", directory);
            FlowBloxVault.Save(new[] { new VaultEntry { Name = "Server", Value = Server } });
            var document = new XmlDocument();
            document.Load(GlobalPaths.GlobalConfigurationXml);
            Assert.IsFalse(document.OuterXml.Contains(Server, StringComparison.Ordinal));
            var stored = OptionElement.FromXml(document.SelectSingleNode("//option_element[@name='Vault.Values']"));
            FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.VaultValues] = stored;
            Assert.AreEqual(Server, FlowBloxVault.Resolve("$Vault::Server"));
            var draft = FlowBloxVault.Load().Single();
            draft.Value = "Not saved";
            Assert.AreEqual(Server, FlowBloxVault.Resolve("$Vault::Server"));
        }
        finally
        {
            var configuration = Path.Combine(directory, "FlowBlox", "global_configuration.xml");
            if (File.Exists(configuration))
                File.Delete(configuration);
            if (Directory.Exists(Path.GetDirectoryName(configuration)))
                Directory.Delete(Path.GetDirectoryName(configuration));
            if (Directory.Exists(directory))
                Directory.Delete(directory);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", previousDirectory);
        }
    }

    [TestMethod]
    [DataRow("Duplicate", "duplicate")]
    [DataRow("Valid", "invalid name")]
    [DataRow("Valid", "")]
    public void Save_RejectsAmbiguousOrInvalidReferencesBeforeChangingStore(string first, string second)
    {
        Assert.ThrowsExactly<ArgumentException>(() => FlowBloxVault.Save(new[]
        {
            new VaultEntry { Name = first, Value = "New" },
            new VaultEntry { Name = second, Value = "New" }
        }));
        Assert.AreEqual(Server, FlowBloxVault.Resolve("$Vault::Server"));
    }
}
