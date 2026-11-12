using FlowBlox.Enums;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Actions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Deletion;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Compression;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.Logic;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Generators;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;

namespace FlowBloxTest.Util
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class FlowBloxDeletionHelperTests
    {
        [TestMethod]
        public void AnalyzeAndAction_TracksRecursiveReferencesAndOwnedFields_WithAtomicUndoRedo()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var source = CreateFormat(project, "Source", "value");
            var decision = new DecisionFlowBlock { Name = "Decision" };
            decision.ReferencedFlowBlocks.Add(source);
            decision.Decisions.Add(new FieldComparisonCondition { FieldElement = source.ResultField });
            project.FlowBloxRegistry.RegisterFlowBlock(decision);
            var expressionOwner = CreateFormat(project, "ExpressionOwner", $"before {source.ResultField.FullyQualifiedName} after");

            var report = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, source);

            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, decision) &&
                x.Impact == DeletionImpact.CollectionEntryRemoved));
            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, decision.Decisions[0]) &&
                x.Impact == DeletionImpact.DirectReferenceRemoved &&
                x.ReviewStatus == DeletionReviewStatus.ManualActionRequired));
            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, expressionOwner) &&
                x.Impact == DeletionImpact.FieldReferenceInvalidated &&
                !x.AutomaticallyRemoved));

            var originalExpression = expressionOwner.FormatExpression;
            var field = source.ResultField;
            var action = FlowBloxDeletionHelper.CreateAction(project.FlowBloxRegistry, report);
            Assert.IsInstanceOfType<FlowBloxDeleteAction>(action);
            Assert.IsTrue(action.AssociatedActions.OfType<FlowBloxEditAction>().Any());
            Assert.IsTrue(action.AssociatedActions.OfType<FlowBloxDisconnectAction>().Any(x =>
                ReferenceEquals(x.From, source) && ReferenceEquals(x.To, decision)));
            action.Invoke();

            Assert.IsFalse(project.FlowBloxRegistry.GetFlowBlocks().Contains(source));
            Assert.IsFalse(project.FlowBloxRegistry.GetManagedObjects().Contains(field));
            Assert.IsFalse(decision.ReferencedFlowBlocks.Contains(source));
            Assert.IsNull(decision.Decisions[0].FieldElement);
            Assert.AreEqual(originalExpression, expressionOwner.FormatExpression);

            action.Undo();
            Assert.IsTrue(project.FlowBloxRegistry.GetFlowBlocks().Contains(source));
            Assert.IsTrue(project.FlowBloxRegistry.GetManagedObjects().Contains(field));
            Assert.IsTrue(decision.ReferencedFlowBlocks.Contains(source));
            Assert.AreSame(field, decision.Decisions[0].FieldElement);
            Assert.AreEqual(originalExpression, expressionOwner.FormatExpression);

            action.Invoke();
            Assert.IsFalse(decision.ReferencedFlowBlocks.Contains(source));
            Assert.IsNull(decision.Decisions[0].FieldElement);
        }

        [TestMethod]
        public void Analyze_DoesNotMutateState_WhenDeletionIsCancelled()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var source = CreateFormat(project, "Source", "value");
            var decision = new DecisionFlowBlock { Name = "Decision" };
            decision.ReferencedFlowBlocks.Add(source);
            decision.Decisions.Add(new FieldComparisonCondition { FieldElement = source.ResultField });
            project.FlowBloxRegistry.RegisterFlowBlock(decision);

            _ = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, source);

            Assert.IsTrue(project.FlowBloxRegistry.GetFlowBlocks().Contains(source));
            Assert.IsTrue(decision.ReferencedFlowBlocks.Contains(source));
            Assert.AreSame(source.ResultField, decision.Decisions[0].FieldElement);
        }

        [TestMethod]
        public void Analyze_TableWriterField_ReportsNestedRequiredReferenceAndRootContext()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var source = CreateFormat(project, "Source", "value");
            var writer = new TableWriterFlowBlock { Name = "Writer" };
            var column = new TableColumnDefinition
            {
                Field = source.ResultField,
                ColumnName = "Value"
            };
            writer.TableColumnDefinitions.Add(column);
            writer.RequiredFields.Add(source.ResultField);
            project.FlowBloxRegistry.RegisterFlowBlock(writer);

            var report = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, source.ResultField);

            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, column) &&
                ReferenceEquals(x.EditTarget, writer) &&
                x.Property.Name == nameof(TableColumnDefinition.Field) &&
                x.ReviewStatus == DeletionReviewStatus.ManualActionRequired));
            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, writer) &&
                x.Property.Name == nameof(writer.RequiredFields) &&
                x.ReviewStatus == DeletionReviewStatus.Information));
        }

        [TestMethod]
        public void Analyze_WithoutExternalDependencies_DoesNotRequireConfirmation()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var source = CreateFormat(project, "Source", "value");

            var report = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, source);

            Assert.IsFalse(report.HasDependencies);
            Assert.AreEqual(0, report.Dependencies.Count);
        }

        [TestMethod]
        public void AnalyzeAndAction_TracksReferencesToOriginalRef_WithUndoRedo()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var original = CreateFormat(project, "Original", "value");
            var replacement = new FormatFlowBlock { Name = "Replacement", FormatExpression = "value" };
            project.FlowBloxRegistry.ReplaceRef(original, replacement);
            var decision = new DecisionFlowBlock { Name = "Decision" };
            decision.ReferencedFlowBlocks.Add(original);
            project.FlowBloxRegistry.RegisterFlowBlock(decision);

            var report = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, replacement);

            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, decision) &&
                ReferenceEquals(x.ReferencedObject, original) &&
                x.Property.Name == nameof(BaseFlowBlock.ReferencedFlowBlocks)));

            var action = FlowBloxDeletionHelper.CreateAction(project.FlowBloxRegistry, report);
            action.Invoke();
            Assert.IsFalse(project.FlowBloxRegistry.GetFlowBlocks().Contains(replacement));
            Assert.IsFalse(decision.ReferencedFlowBlocks.Contains(original));

            action.Undo();
            Assert.IsTrue(project.FlowBloxRegistry.GetFlowBlocks().Contains(original));
            Assert.IsTrue(decision.ReferencedFlowBlocks.Contains(original));

            action.Invoke();
            Assert.IsFalse(project.FlowBloxRegistry.GetFlowBlocks().Contains(original));
            Assert.IsFalse(decision.ReferencedFlowBlocks.Contains(original));
        }

        [TestMethod]
        public void AnalyzeAndAction_TracksManagedObjectOriginalRef()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var original = new ZipArchiveObject { Name = "Original archive" };
            var replacement = new ZipArchiveObject { Name = "Replacement archive" };
            project.FlowBloxRegistry.RegisterManagedObject(original);
            project.FlowBloxRegistry.ReplaceRef(original, replacement);
            var owner = new ZipArchiveCreatorFlowBlock
            {
                Name = "Archive creator",
                ZipArchiveObject = original
            };
            project.FlowBloxRegistry.RegisterFlowBlock(owner);

            var report = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, replacement);

            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, owner) &&
                ReferenceEquals(x.ReferencedObject, original) &&
                x.Property.Name == nameof(ZipArchiveCreatorFlowBlock.ZipArchiveObject)));

            var action = FlowBloxDeletionHelper.CreateAction(project.FlowBloxRegistry, report);
            action.Invoke();
            Assert.IsNull(owner.ZipArchiveObject);
            Assert.IsFalse(project.FlowBloxRegistry.GetManagedObjects().Contains(replacement));

            action.Undo();
            Assert.AreSame(original, owner.ZipArchiveObject);
            Assert.IsTrue(project.FlowBloxRegistry.GetManagedObjects().Contains(original));
        }

        [TestMethod]
        public void AnalyzeAndAction_ResultFieldRow_IncludesOwnedFieldDependencies()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var owner = new ZipArchiveFileIteratorFlowBlock { Name = "Iterator" };
            var field = new FieldElement(owner) { Name = "Content" };
            var row = new ResultFieldByEnumValue<ZipArchiveIteratorDestinations>
            {
                EnumValue = ZipArchiveIteratorDestinations.Content,
                ResultField = field
            };
            owner.ResultFields.Add(row);
            project.FlowBloxRegistry.RegisterFlowBlock(owner);
            project.FlowBloxRegistry.RegisterManagedObject(field);
            var decision = new DecisionFlowBlock { Name = "Decision" };
            decision.Decisions.Add(new FieldComparisonCondition { FieldElement = field });
            project.FlowBloxRegistry.RegisterFlowBlock(decision);
            var expressionOwner = CreateFormat(project, "ExpressionOwner", field.FullyQualifiedName);

            var report = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, row);

            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, owner) &&
                x.Property.Name == nameof(ZipArchiveFileIteratorFlowBlock.ResultFields) &&
                x.Impact == DeletionImpact.CollectionEntryRemoved));
            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, decision.Decisions[0]) &&
                ReferenceEquals(x.ReferencedObject, field) &&
                x.Impact == DeletionImpact.DirectReferenceRemoved));
            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, expressionOwner) &&
                ReferenceEquals(x.ReferencedObject, field) &&
                x.Impact == DeletionImpact.FieldReferenceInvalidated));

            var action = FlowBloxDeletionHelper.CreateAction(project.FlowBloxRegistry, report);
            action.Invoke();
            Assert.IsFalse(owner.ResultFields.Contains(row));
            Assert.IsNull(decision.Decisions[0].FieldElement);
            Assert.IsFalse(project.FlowBloxRegistry.GetManagedObjects().Contains(field));
            Assert.AreEqual(field.FullyQualifiedName, expressionOwner.FormatExpression);

            action.Undo();
            Assert.IsTrue(owner.ResultFields.Contains(row));
            Assert.AreSame(field, decision.Decisions[0].FieldElement);
            Assert.IsTrue(project.FlowBloxRegistry.GetManagedObjects().Contains(field));
        }

        [TestMethod]
        public void Analyze_ResultFieldRowInPropertyContext_ExcludesOwnerStructureButKeepsExternalDependencies()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var owner = new ZipArchiveFileIteratorFlowBlock { Name = "Iterator" };
            var field = new FieldElement(owner) { Name = "Content" };
            var row = new ResultFieldByEnumValue<ZipArchiveIteratorDestinations>
            {
                EnumValue = ZipArchiveIteratorDestinations.Content,
                ResultField = field
            };
            owner.ResultFields.Add(row);
            owner.ActivationConditions.Add(new FieldLogicalComparisonCondition { FieldElement = field });
            project.FlowBloxRegistry.RegisterFlowBlock(owner);
            project.FlowBloxRegistry.RegisterManagedObject(field);
            var decision = new DecisionFlowBlock { Name = "Decision" };
            decision.Decisions.Add(new FieldComparisonCondition { FieldElement = field });
            project.FlowBloxRegistry.RegisterFlowBlock(decision);

            var report = FlowBloxDeletionHelper.Analyze(
                project.FlowBloxRegistry,
                new object[] { row },
                new object[] { owner });

            Assert.IsFalse(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, owner) &&
                (x.Property.Name == nameof(owner.ResultFields) ||
                 x.Property.Name == nameof(owner.Fields) ||
                 x.Property.Name == nameof(owner.DefinedManagedObjects))));
            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, owner.ActivationConditions[0]) &&
                ReferenceEquals(x.ReferencedObject, field)));
            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, decision.Decisions[0]) &&
                ReferenceEquals(x.ReferencedObject, field)));
        }

        [TestMethod]
        public void ReactiveDeletionObjects_ToStringUsesDomainName()
        {
            var strategy = new SequenceDetectionGenerationStrategy { Name = "Generate categories" };
            var testDefinition = new FlowBloxTestDefinition { Name = "Category tests" };
            var dataset = new FlowBlockTestDataset { ParentTestDefinition = testDefinition };

            Assert.AreEqual("Generate categories", strategy.ToString());
            Assert.AreEqual("Category tests", dataset.ToString());
        }

        [TestMethod]
        public void FlowBloxUIAttribute_PreservesMissingReferenceForRuntimeDiagnostics()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var owner = CreateFormat(project, "Owner", "before $Deleted::Field0 after");
            var original = owner.FormatExpression;
            var context = new ValidationContext(owner) { MemberName = nameof(FormatFlowBlock.FormatExpression) };
            var results = new List<ValidationResult>();

            var valid = Validator.TryValidateProperty(owner.FormatExpression, context, results);

            Assert.IsTrue(valid);
            Assert.AreEqual(original, owner.FormatExpression);
            Assert.IsEmpty(results);
        }

        [TestMethod]
        public async Task AiDelete_ReturnsCompleteStructuredDeletionReport()
        {
            var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            try
            {
                var source = CreateFormat(project, "Source", "value");
                var decision = new DecisionFlowBlock { Name = "Decision" };
                decision.ReferencedFlowBlocks.Add(source);
                decision.Decisions.Add(new FieldComparisonCondition { FieldElement = source.ResultField });
                project.FlowBloxRegistry.RegisterFlowBlock(decision);
                _ = CreateFormat(project, "ExpressionOwner", source.ResultField.FullyQualifiedName);

                var response = await new DefaultToolApi().ExecuteAsync(
                    new ToolRequest
                    {
                        ToolName = "DeleteFlowBlock",
                        Arguments = new JObject { ["name"] = source.Name }
                    },
                    CancellationToken.None);

                Assert.IsTrue(response.Ok, response.Error);
                var dependencies = (JArray)response.Result["deletionReport"];
                Assert.IsTrue(dependencies.Count >= 3);
                Assert.IsTrue(dependencies.Any(x => x.Value<string>("property") == nameof(DecisionFlowBlock.ReferencedFlowBlocks)));
                Assert.IsTrue(dependencies.Any(x =>
                    x.Value<string>("impact") == nameof(DeletionImpact.FieldReferenceInvalidated) &&
                    x.Value<string>("reviewStatus") == nameof(DeletionReviewStatus.ManualActionRequired) &&
                    x.Value<bool>("automaticallyRemoved") == false));
            }
            finally
            {
                FlowBloxProjectManager.Instance.ActiveProject = previousProject;
            }
        }

        [TestMethod]
        public async Task AiDelete_WithoutDependencies_OmitsDeletionReport()
        {
            var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            try
            {
                var source = CreateFormat(project, "Source", "value");

                var response = await new DefaultToolApi().ExecuteAsync(
                    new ToolRequest
                    {
                        ToolName = "DeleteFlowBlock",
                        Arguments = new JObject { ["name"] = source.Name }
                    },
                    CancellationToken.None);

                Assert.IsTrue(response.Ok, response.Error);
                Assert.IsTrue(response.Result.Value<bool>("deleted"));
                Assert.IsNull(response.Result["deletionReport"]);
            }
            finally
            {
                FlowBloxProjectManager.Instance.ActiveProject = previousProject;
            }
        }

        [TestMethod]
        public void DeleteFlowBlock_PreservesTestDefinitionSpecialHandling()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var source = CreateFormat(project, "Source", "value");
            var testDefinition = new FlowBloxTestDefinition { Name = "Test" };
            var dataset = new FlowBlockTestDataset { FlowBlock = source };
            dataset.FlowBloxTestConfigurations.Add(new FlowBloxFieldTestConfiguration
            {
                FieldElement = source.ResultField
            });
            testDefinition.Entries.Add(dataset);
            testDefinition.OnAfterLoad();
            project.FlowBloxRegistry.RegisterManagedObject(testDefinition);

            var report = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, source);
            Assert.IsTrue(report.Dependencies.Any(x =>
                ReferenceEquals(x.ReferencingObject, testDefinition) &&
                ReferenceEquals(x.ReferencedObject, dataset) &&
                x.Property.Name == nameof(FlowBloxTestDefinition.Entries) &&
                x.Impact == DeletionImpact.CollectionEntryRemoved));
            var action = FlowBloxDeletionHelper.CreateAction(project.FlowBloxRegistry, report);
            Assert.IsTrue(action.AssociatedActions.OfType<FlowBloxEditAction>()
                .Any(x => ReferenceEquals(x.Target, testDefinition)));
            action.Invoke();

            Assert.IsFalse(testDefinition.Entries.Contains(dataset));
            action.Undo();
            Assert.IsTrue(testDefinition.Entries.Contains(dataset));
            Assert.AreSame(source, dataset.FlowBlock);
            Assert.AreSame(source.ResultField, dataset.FlowBloxTestConfigurations.Single().FieldElement);
        }

        private static FormatFlowBlock CreateFormat(FlowBloxProject project, string name, string expression)
        {
            var block = new FormatFlowBlock { Name = name, FormatExpression = expression };
            project.FlowBloxRegistry.PostProcessFlowBlockCreated(block);
            project.FlowBloxRegistry.RegisterFlowBlock(block);
            return block;
        }
    }
}
