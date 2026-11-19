using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.Components;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FieldElementDefaultValueTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("Explicit input")]
    public void CopyAndApply_DefaultChangesPreserveFallbackAndExplicitInput(string? input)
    {
        var field = new FieldElement(FieldTypes.Text)
        {
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            DefaultValue = "$Project::InputDirectory/knowledge.sqlite",
            StringValue = input
        };
        var copier = new DynamicDeepCopier();
        var edited = (FieldElement)copier.Copy(field);
        string? notifiedValue = null;
        edited.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FieldElement.StringValue))
                notifiedValue = edited.StringValue;
        };

        edited.DefaultValue = "$Project::InputDirectory/updated.sqlite";

        Assert.AreEqual(input ?? edited.DefaultValue, notifiedValue);
        Assert.AreEqual(input, edited.LocalStringValue);
        copier.SpecialMode = DeepCopierSpecialMode.Recopy;
        copier.Copy(edited, field);
        field.DefaultValue = "$Project::InputDirectory/final.sqlite";
        Assert.AreEqual(input ?? field.DefaultValue, field.StringValue);
        Assert.AreEqual(input, field.LocalStringValue);
    }

    [TestMethod]
    public void WritingDisplayedDefault_DoesNotPinInputToPreviousDefault()
    {
        var field = new FieldElement(FieldTypes.Text)
        {
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            DefaultValue = "Original"
        };

        field.StringValue = field.StringValue;
        field.DefaultValue = "Updated";

        Assert.AreEqual("Updated", field.StringValue);
        Assert.IsNull(field.LocalStringValue);
    }

    [TestMethod]
    [DataRow("{\"StringValue\":\"Legacy\",\"UserFieldType\":\"Input\",\"UserField\":true,\"StoreValueLocally\":false}", "Legacy")]
    [DataRow("{\"StringValue\":\"Legacy\",\"UserField\":true,\"UserFieldType\":\"Input\",\"DefaultValue\":\"New default\"}", "New default")]
    public void Deserialize_MigratesLegacyInputAfterAllPropertiesAreRead(string json, string expected)
    {
        var field = JsonConvert.DeserializeObject<FieldElement>(json)!;

        Assert.AreEqual(expected, field.DefaultValue);
        Assert.IsNull(field.LocalStringValue);
        field.StringValue = "Override";
        field.StringValue = "";
        Assert.AreEqual(expected, field.StringValue);

        var exported = JObject.Parse(JsonConvert.SerializeObject(field, JsonSettings.ProjectExport()));
        Assert.IsNull(exported["StringValue"]);
        Assert.IsNull(exported["StoreValueLocally"]);
        Assert.AreEqual(expected, (string?)exported["DefaultValue"]);
    }

    [TestMethod]
    [DataRow("{\"StringValue\":\"Secret\",\"DefaultValue\":\"Secret\",\"UserField\":true,\"UserFieldType\":\"Input\",\"IsPassword\":true}")]
    [DataRow("{\"IsPassword\":true,\"UserField\":true,\"UserFieldType\":\"Input\",\"DefaultValue\":\"Secret\",\"StringValue\":\"Secret\"}")]
    public void Deserialize_PasswordNeverBecomesProjectDefault(string json)
    {
        var field = JsonConvert.DeserializeObject<FieldElement>(json)!;

        Assert.IsNull(field.DefaultValue);
        Assert.IsNull(field.LocalStringValue);
        field.StringValue = "Local password";
        var exported = JObject.Parse(JsonConvert.SerializeObject(field, JsonSettings.ProjectExport()));
        Assert.IsNull(exported["StringValue"]);
        Assert.IsNull(exported["DefaultValue"]);
    }

    [TestMethod]
    [DataRow(true, UserFieldTypes.Input, false)]
    [DataRow(true, UserFieldTypes.Memory, true)]
    [DataRow(false, UserFieldTypes.Memory, false)]
    public void Serialize_OnlyMemoryFieldsPersistCurrentValues(bool userField, UserFieldTypes type, bool persists)
    {
        var field = new FieldElement(FieldTypes.Text)
        {
            UserField = userField,
            UserFieldType = type,
            StringValue = "Current value"
        };

        var exported = JObject.Parse(JsonConvert.SerializeObject(field, JsonSettings.ProjectExport()));

        Assert.AreEqual(persists, exported.ContainsKey("StringValue"));
        if (persists)
            Assert.AreEqual("Current value", (string?)exported["StringValue"]);
    }

    [TestMethod]
    public void SaveAndLoad_KeepsOverridesLocalAndDoesNotFreezeFallbackAsOverride()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "TestResults", "field-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Default.fbprj");
        try
        {
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "Input");
            field.DefaultValue = "Default";
            field.StringValue = "Local override";
            project.Save(path);

            Assert.IsFalse(File.ReadAllText(path).Contains("Local override", StringComparison.Ordinal));
            var loaded = FlowBloxProject.FromFile(path);
            FlowBloxProjectManager.Instance.ActiveProject = loaded;
            var loadedField = loaded.UserFields.Single();
            Assert.AreEqual("Default", loadedField.DefaultValue);
            Assert.AreEqual("Local override", loadedField.StringValue);

            loadedField.StringValue = "";
            loaded.Save(path);
            var reloaded = FlowBloxProject.FromFile(path);
            var reloadedField = reloaded.UserFields.Single();
            reloadedField.DefaultValue = "Updated default";
            Assert.AreEqual("Updated default", reloadedField.StringValue);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RuntimeBoundValues_RemainIsolatedBetweenThreadsWithSharedDefault()
    {
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        var start = project.FlowBloxRegistry.CreateFlowBlockUnregistered<StartFlowBlock>();
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(start);
        project.FlowBloxRegistry.Register(start);
        var field = new FieldElement(FieldTypes.Text)
        {
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            DefaultValue = "Default",
            StringValue = "Local"
        };
        using var barrier = new Barrier(2);
        var tasks = new[] { "First", "Second" }.Select(value => Task.Run(() =>
        {
            var runtime = new TransientRuntime(project);
            field.SetValueWithoutEvaluation(runtime, value, runtimeBound: true);
            Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            Assert.AreEqual(value, field.StringValue);
            field.ReleaseRuntimeValue(runtime);
            Assert.AreEqual("Local", field.StringValue);
        })).ToArray();

        await Task.WhenAll(tasks);
        field.StringValue = "";
        Assert.AreEqual("Default", field.StringValue);
    }

    [TestMethod]
    public void Validation_RejectsDisallowedDefaultEvenWhenCurrentInputIsAllowed()
    {
        var field = new FieldElement(FieldTypes.Text)
        {
            Name = "Choice",
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            DefaultValue = "Other",
            StringValue = "Allowed",
            LoVType = LoVType.PossibleValues,
            ListOfValues = [new ValueItem { Value = "Allowed" }]
        };

        Assert.IsTrue(field.ValidateObject().ContainsKey(nameof(FieldElement.DefaultValue)));
        field.LoVType = LoVType.Suggestion;
        Assert.IsFalse(field.ValidateObject().ContainsKey(nameof(FieldElement.DefaultValue)));
        field.LoVType = LoVType.PossibleValues;
        field.DefaultValue = "Allowed";
        Assert.IsFalse(field.ValidateObject().ContainsKey(nameof(FieldElement.DefaultValue)));
    }
}
