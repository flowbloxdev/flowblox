using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.Compression;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class TypeKindsInfoHandlerTests
    {
        [TestMethod]
        public void GenericNameHelper_CompactsNestedGenericTypesRecursively()
        {
            var compactName = AiAssistantGenericTypeNameHelper.GetCompactTypeName(
                typeof(Dictionary<string, ObservableCollection<FlowBlox.Core.Models.Components.FieldElement>>));

            Assert.AreEqual(
                "System.Collections.Generic.Dictionary<System.String,System.Collections.ObjectModel.ObservableCollection<FMC.FieldElement>>",
                compactName);
        }

        [TestMethod]
        public async Task DerivedFlowBlockMetadata_ExposesActivationConditionTypes()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            ToolHandlerUtilities.ClearSessionCache();

            var response = await new GetTypeKindsInfoHandler().HandleAsync(
                new JObject
                {
                    ["typeFullName"] = typeof(TableReaderFlowBlock).FullName
                },
                CancellationToken.None);

            Assert.IsTrue(response.Ok, response.Error);
            var activationConditions = response.Result["kind"]?["properties"]?
                .Children<JObject>()
                .SingleOrDefault(x => x.Value<string>("name") == "ActivationConditions");

            Assert.IsNotNull(activationConditions);
            Assert.AreEqual("ReactiveObject", activationConditions.Value<string>("majorTypeDescriptor"));

            var creatableTypes = activationConditions["ui"]?["creatableTypes"]?.Values<string>().ToList();
            CollectionAssert.AreEquivalent(
                new[]
                {
                    Compact(typeof(FieldLogicalComparisonCondition)),
                    Compact(typeof(LogicalGroupCondition))
                },
                creatableTypes);

            var logicalConditionType = response.Result["kind"]?["usedTypes"]?
                .Children<JObject>()
                .SingleOrDefault(x => x.Value<string>("fullName") == Compact(typeof(LogicalCondition)));
            Assert.IsNotNull(logicalConditionType);

            var supportedTypes = logicalConditionType["supportedTypes"]?
                .Children<JObject>()
                .Select(x => x.Value<string>("fullName"))
                .ToList();
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    Compact(typeof(FieldLogicalComparisonCondition)),
                    Compact(typeof(LogicalGroupCondition))
                },
                supportedTypes);

        }

        [TestMethod]
        public async Task DerivedFlowBlockMetadata_AlwaysIncludesBaseMembers()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            ToolHandlerUtilities.ClearSessionCache();
            var response = await new GetTypeKindsInfoHandler().HandleAsync(
                new JObject
                {
                    ["typeFullName"] = typeof(TableReaderFlowBlock).FullName
                },
                CancellationToken.None);
            Assert.IsTrue(response.Ok, response.Error);
            var names = response.Result["kind"]!["properties"]!.Children<JObject>()
                .Select(x => x.Value<string>("name"))
                .ToArray();
            CollectionAssert.IsSubsetOf(
                new[] { "MappingEntries",
                    "ReferencedTable",
                    "OutputIgnoreDuplicates",
                    "ActivationConditions" },
                names);
            CollectionAssert.DoesNotContain(names, "DefinedManagedObjects");
            CollectionAssert.DoesNotContain(names, "Location");
            CollectionAssert.DoesNotContain(names, "TestDefinitions");
        }
        [TestMethod]
        public async Task DerivedFlowBlockMetadata_IncludesIntermediateDomainBaseMembersByDefault()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            ToolHandlerUtilities.ClearSessionCache();

            var response = await new GetTypeKindsInfoHandler().HandleAsync(
                new JObject
                {
                    ["typeFullName"] = typeof(WebEventFlowBlock).FullName
                },
                CancellationToken.None);

            Assert.IsTrue(response.Ok, response.Error);
            var propertyNames = response.Result["kind"]?["properties"]?
                .Children<JObject>()
                .Select(x => x.Value<string>("name"))
                .ToList();

            CollectionAssert.Contains(propertyNames, "AssociatedWebBrowser");
            CollectionAssert.Contains(propertyNames, "OutputIgnoreDuplicates");
            CollectionAssert.Contains(propertyNames, "ResultField");
        }

        [TestMethod]
        public async Task AbstractFlowBlockBaseTypes_CanBeQueriedDirectly()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            ToolHandlerUtilities.ClearSessionCache();

            var baseTypes = new[]
            {
                typeof(FlowBlox.Core.Models.FlowBlocks.Base.BaseFlowBlock),
                typeof(FlowBlox.Core.Models.FlowBlocks.Base.BaseResultFlowBlock),
                typeof(FlowBlox.Core.Models.FlowBlocks.Base.BaseSingleResultFlowBlock),
                typeof(FlowBlox.Core.Models.FlowBlocks.Base.BasePipeFlowBlock),
                typeof(FlowBlox.Core.Models.FlowBlocks.Base.BaseMultiResultCollectorFlowBlock),
                typeof(FlowBlox.Core.Models.FlowBlocks.Base.BaseSingleResultCollectorFlowBlock)
            };

            var handler = new GetTypeKindsInfoHandler();
            foreach (var baseType in baseTypes)
            {
                Assert.IsTrue(baseType.IsAbstract, $"Test setup expects '{baseType.FullName}' to be abstract.");

                var response = await handler.HandleAsync(
                    new JObject
                    {
                        ["typeFullName"] = baseType.FullName
                    },
                    CancellationToken.None);

                Assert.IsTrue(response.Ok, $"{baseType.FullName}: {response.Error}");
                Assert.AreEqual(Compact(baseType), response.Result["kind"]?["fullName"]?.Value<string>());
            }
        }

        [TestMethod]
        public async Task ComponentMetadata_HidesFrameworkOnlyPropertiesForManagedObjects()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            ToolHandlerUtilities.ClearSessionCache();

            var response = await new GetTypeKindsInfoHandler().HandleAsync(
                new JObject
                {
                    ["typeFullName"] = typeof(SQLTable).FullName
                },
                CancellationToken.None);

            Assert.IsTrue(response.Ok, response.Error);
            var propertyNames = response.Result["kind"]?["properties"]?
                .Children<JObject>()
                .Select(x => x.Value<string>("name"))
                .ToList();

            CollectionAssert.Contains(propertyNames, "Name");
            CollectionAssert.Contains(propertyNames, "RequiredFields");
            CollectionAssert.DoesNotContain(propertyNames, "HandleRequirements");
            CollectionAssert.DoesNotContain(propertyNames, "HasErrors");
            CollectionAssert.DoesNotContain(propertyNames, "Icon16");
            CollectionAssert.DoesNotContain(propertyNames, "Icon32");
            CollectionAssert.DoesNotContain(propertyNames, "Version");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ScalarMetadata_ReportsSelectionDefaultsWithoutObjectOperations(bool managedObjectTool)
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            ToolHandlerUtilities.ClearSessionCache();
            ToolHandlerBase handler = managedObjectTool
                ? new GetManagedObjectKindsInfoHandler()
                : new GetTypeKindsInfoHandler();

            var response = await handler.HandleAsync(
                new JObject { ["typeFullName"] = typeof(SQLTable).FullName },
                CancellationToken.None);

            Assert.IsTrue(response.Ok, response.Error);
            var properties = response.Result["kind"]!["properties"]!.Children<JObject>()
                .ToDictionary(property => property.Value<string>("name")!);
            Assert.AreEqual("[FLD]", properties[nameof(SQLTable.SQLStatement)].Value<string>("uiFlags"));
            Assert.AreEqual("[FLD]", properties[nameof(SQLTable.DbType)].Value<string>("uiFlags"));
            Assert.IsNull(properties[nameof(SQLTable.UseFieldValuesAsParameters)]["uiFlags"]);
            Assert.AreEqual("DBConnection", properties[nameof(SQLTable.SQLConnectionstring)]["ui"]?["toolboxCategory"]?.Value<string>());
        }

        [TestMethod]
        public async Task AssociationMetadata_RetainsExplicitOperationsAndCollectionDefaultsWithoutGeneralUiAnnotation()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            ToolHandlerUtilities.ClearSessionCache();
            var response = await new GetTypeKindsInfoHandler().HandleAsync(
                new JObject { ["typeFullName"] = typeof(TableReaderFlowBlock).FullName },
                CancellationToken.None);

            Assert.IsTrue(response.Ok, response.Error);
            var properties = response.Result["kind"]!["properties"]!.Children<JObject>().ToArray();
            var reference = properties.Single(property => property.Value<string>("name") == nameof(TableReaderFlowBlock.ReferencedTable));
            var collection = properties.Single(property => property.Value<string>("name") == nameof(TableReaderFlowBlock.MappingEntries));
            Assert.AreEqual("CLUED", reference.Value<string>("uiFlags"));
            Assert.AreEqual("CLUED", collection.Value<string>("uiFlags"));
            var nested = response.Result["additionalTypeKindsInfo"]!.Children<JObject>()
                .Single(type => type.Value<string>("fullName") == Compact(typeof(TableSelectorColumnCondition)));
            var columnType = nested["properties"]!.Children<JObject>()
                .Single(property => property.Value<string>("name") == nameof(TableSelectorColumnCondition.ColumnType));
            Assert.AreEqual("E", columnType.Value<string>("uiFlags"));
        }

        private static string Compact(Type type) =>
            AiAssistantTypeAliasHelper.CompressTypeName(type.FullName!);

    }
}
