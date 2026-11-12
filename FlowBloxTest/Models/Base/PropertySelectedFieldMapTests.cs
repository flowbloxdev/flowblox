using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.Communication;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Json;
using FlowBlox.Core.Util;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Deletion;
using FlowBlox.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBloxTest.Runtime;

namespace FlowBloxTest.Models.Base
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class PropertySelectedFieldMapTests
    {
        [TestMethod]
        public void LegacyProject_ImportsIntoMapAndExportsOnlyMap()
        {
            const string legacyJson = "{\"EncodingName_SelectedField\":{\"Name\":\"Encoding\",\"UserField\":true}}";
            var block = JsonConvert.DeserializeObject<FileReaderFlowBlock>(legacyJson, JsonSettings.ProjectImport(new()))!;

            Assert.AreEqual("$User::Encoding", block.GetPropertySelectedFieldName(nameof(block.EncodingName)));
            var exported = JObject.Parse(JsonConvert.SerializeObject(block, JsonSettings.ProjectExport()));
            Assert.IsNull(exported["EncodingName_SelectedField"]);
            Assert.AreEqual("$User::Encoding", (string?)exported[nameof(block.PropertySelectedFieldMap)]?[nameof(block.EncodingName)]);

            var reloaded = JsonConvert.DeserializeObject<FileReaderFlowBlock>(exported.ToString(), JsonSettings.ProjectImport(new()))!;
            Assert.AreEqual(block.GetPropertySelectedFieldName(nameof(block.EncodingName)), reloaded.GetPropertySelectedFieldName(nameof(block.EncodingName)));
        }

        [TestMethod]
        public void LegacyImport_ReplacesExistingMapAssignment()
        {
            var registry = new FlowBloxRegistry();
            var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Port");
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
            var block = new SMTPFlowBlock();
            block.SetPropertySelectedFieldName(nameof(block.Port), "$User::PreviousPort");

            JsonConvert.PopulateObject(
                "{\"Port_SelectedField\":{\"Name\":\"Port\",\"UserField\":true}}",
                block,
                JsonSettings.ProjectImport(new()));

            Assert.AreEqual(1, block.PropertySelectedFieldMap.Count);
            Assert.AreEqual(field.FullyQualifiedName, block.GetPropertySelectedFieldName(nameof(block.Port)));
            Assert.AreSame(field, block.GetPropertySelectedField(nameof(block.Port)));
        }

        [TestMethod]
        public void LegacyImport_ResolvesJsonReferencesWithoutCompanionProperties()
        {
            var document = new JArray
            {
                new JObject
                {
                    ["$id"] = "port",
                    ["$type"] = typeof(FieldElement).AssemblyQualifiedName,
                    ["Name"] = "Port",
                    ["UserField"] = true
                },
                new JObject
                {
                    ["$type"] = typeof(SMTPFlowBlock).AssemblyQualifiedName,
                    ["Port_SelectedField"] = new JObject { ["$ref"] = "port" }
                }
            };

            var objects = JsonConvert.DeserializeObject<object[]>(document.ToString(), JsonSettings.ProjectImport(new()))!;
            var field = (FieldElement)objects[0];
            var block = (SMTPFlowBlock)objects[1];
            Assert.AreEqual(field.FullyQualifiedName, block.GetPropertySelectedFieldName(nameof(block.Port)));
        }

        [TestMethod]
        public void DeletionReview_TracksSelectionsOnNullablePropertiesWithNullLiterals()
        {
            var registry = new FlowBloxRegistry();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
            var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Since");
            var block = new IMAPMailFetchFlowBlock { Name = "Mail", StartDate = null };
            registry.RegisterFlowBlock(block);
            block.SetPropertySelectedField(nameof(block.StartDate), field);

            var report = FlowBloxDeletionHelper.Analyze(registry, field);
            Assert.IsTrue(report.Dependencies.Any(dependency =>
                ReferenceEquals(dependency.ReferencingObject, block)
                && dependency.Property.Name == nameof(block.StartDate)
                && dependency.Impact == DeletionImpact.FieldReferenceInvalidated
                && !dependency.AutomaticallyRemoved));
            Assert.AreEqual(field.FullyQualifiedName, block.GetPropertySelectedFieldName(nameof(block.StartDate)));
        }

        [TestMethod]
        public void Selection_OverwritesNotifiesAndUnlinksWithoutChangingLiteral()
        {
            var block = new SMTPFlowBlock { Port = 25 };
            var notifications = new List<string?>();
            block.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

            block.SetPropertySelectedFieldName(nameof(block.Port), "$USER::First");
            block.SetPropertySelectedFieldName(nameof(block.Port), "$USER::Second");
            Assert.AreEqual("$USER::Second", block.PropertySelectedFieldMap[nameof(block.Port)]);
            Assert.IsTrue(notifications.Contains(nameof(block.Port)));
            Assert.IsTrue(notifications.Contains(nameof(block.PropertySelectedFieldMap)));

            block.SetPropertySelectedFieldName(nameof(block.Port), null!);
            Assert.AreEqual(0, block.PropertySelectedFieldMap.Count);
            Assert.AreEqual(25, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(block, x => x.Port));
        }

        [TestMethod]
        public void Runtime_ResolvesCurrentRegistryAndPreservesUnresolvedSelection()
        {
            var registry = new FlowBloxRegistry();
            var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Encoding");
            field.StringValue = "UTF8";
            var block = new FileReaderFlowBlock { EncodingName = DotNetEncodingNames.ASCII };
            block.SetPropertySelectedField(nameof(block.EncodingName), field);

            using (FlowBloxRegistryProvider.BeginScopedRegistry(registry))
            {
                Assert.AreEqual(DotNetEncodingNames.UTF8, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(block, x => x.EncodingName));
                Assert.AreSame(field, FlowBloxFieldHelper.GetPropertyValueOrSelectedField(block, typeof(FileReaderFlowBlock).GetProperty(nameof(block.EncodingName))!));
            }

            using (FlowBloxRegistryProvider.BeginScopedRegistry(new FlowBloxRegistry()))
            {
                Assert.AreEqual(DotNetEncodingNames.ASCII, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(block, x => x.EncodingName));
                Assert.AreEqual(field.FullyQualifiedName, block.GetPropertySelectedFieldName(nameof(block.EncodingName)));
            }
        }

        [TestMethod]
        public void TransactionCopy_IsolatesSelectionsAndCommitsUnlink()
        {
            var original = new FlowBloxReactiveObject();
            original.SetPropertySelectedFieldName("Enabled", "$USER::Enabled");
            var copier = new DynamicDeepCopier();
            var copy = (FlowBloxReactiveObject)copier.Copy(original);
            copy.SetPropertySelectedFieldName("Enabled", null!);

            Assert.AreEqual("$USER::Enabled", original.GetPropertySelectedFieldName("Enabled"));
            copier.SpecialMode = DeepCopierSpecialMode.Recopy;
            copier.Copy(copy);
            Assert.IsNull(original.GetPropertySelectedFieldName("Enabled"));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void Rename_UpdatesDirectAndNestedMapEntries(bool renameSource, bool useDerivedFieldName)
        {
            var registry = new FlowBloxRegistry();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
            var source = new FormatFlowBlock();
            source.Name = source.NamePrefix + "Original";
            registry.RegisterFlowBlock(source);
            var field = new FieldElement(source, FieldNameGenerationMode.DeriveFromFlowBlock, FieldTypes.Text);
            source.Fields.Add(field);
            field.OnAfterCreate();
            if (!useDerivedFieldName)
                field.Name = "CustomField";
            registry.Register(field);
            var otherField = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "OtherField");
            var otherFieldName = otherField.FullyQualifiedName;
            var owner = new ScalarFieldProbeFlowBlock { Name = "Consumer" };
            owner.SetPropertySelectedField(nameof(owner.Count), field);
            owner.SetPropertySelectedField(nameof(owner.Encoding), field);
            owner.SetPropertySelectedField(nameof(owner.Since), otherField);
            owner.Settings.SetPropertySelectedField(nameof(owner.Settings.Limit), field);
            owner.RegisterPropertyChangedEventHandlers();
            var notifications = new List<string>();
            var nestedNotifications = new List<string>();
            owner.PropertyChanged += (_, args) => notifications.Add(args.PropertyName!);
            owner.Settings.PropertyChanged += (_, args) => nestedNotifications.Add(args.PropertyName!);

            if (renameSource)
                source.Name = source.NamePrefix + "Renamed";
            else
                field.Name = "RenamedField";

            Assert.AreEqual(field.FullyQualifiedName, owner.GetPropertySelectedFieldName(nameof(owner.Count)));
            Assert.AreEqual(field.FullyQualifiedName, owner.GetPropertySelectedFieldName(nameof(owner.Encoding)));
            Assert.AreEqual(field.FullyQualifiedName, owner.Settings.GetPropertySelectedFieldName(nameof(owner.Settings.Limit)));
            Assert.AreEqual(otherFieldName, owner.GetPropertySelectedFieldName(nameof(owner.Since)));
            Assert.AreSame(field, owner.Settings.GetPropertySelectedField(nameof(owner.Settings.Limit)));
            CollectionAssert.Contains(notifications, nameof(owner.PropertySelectedFieldMap));
            CollectionAssert.Contains(notifications, nameof(owner.Count));
            CollectionAssert.Contains(nestedNotifications, nameof(owner.Settings.Limit));
            Assert.AreEqual(7, owner.Count);
            Assert.AreEqual(10, owner.Settings.Limit);
        }

        [TestMethod]
        public void AssociatedFields_IncludeMapSelectionsAndFollowRenames()
        {
            var registry = new FlowBloxRegistry();
            var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Encoding");
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
            var block = new FileReaderFlowBlock();
            block.SetPropertySelectedField(nameof(block.EncodingName), field);
            CollectionAssert.Contains(block.GetAssociatedFields().ToList(), field);
            block.RegisterPropertyChangedEventHandlers();

            field.Name = "RenamedEncoding";

            Assert.AreEqual(field.FullyQualifiedName, block.GetPropertySelectedFieldName(nameof(block.EncodingName)));
        }
    }
}
