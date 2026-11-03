using FlowBlox.Core.Models.Deletion;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Json;
using FlowBlox.UICore.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.Util;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class DeletionHistoryRestorationTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void DeleteUndo_BranchedWebRequest_RestoresConfigurationAndDependencyReport(bool explicitContext, bool deleteUpstream)
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var start = Add(project, new StartFlowBlock { Name = "Start" });
        var list = Add(project, new WebRequestFlowBlock { Name = "WebRequest_ProductList" });
        list.ReferencedFlowBlocks.Add(start);
        var links = Add(project, new StartEndPatternSelectorFlowBlock { Name = "StartEndPatternSelector_ProductLinks" });
        links.ReferencedFlowBlocks.Add(list);
        links.InputField = list.Fields.First();
        var page = Add(project, new WebRequestFlowBlock { Name = "WebRequest_ProductPage" });
        page.ReferencedFlowBlocks.Add(links);
        var writer = Add(project, new TableWriterFlowBlock { Name = "TableWriter_Output" });

        foreach (var suffix in new[] { "Title", "Price", "Description" })
        {
            var selector = Add(project, new StartEndPatternSelectorFlowBlock { Name = "StartEndPatternSelector_" + suffix });
            selector.ReferencedFlowBlocks.Add(page);
            selector.InputField = page.Fields.First();
            if (explicitContext)
                selector.AssociatedIterationContext = start;
            selector.AutomaticInputBehaviorAssignment = suffix != "Description";

            // The supplied project contains persisted assignments even where the
            // iteration context is currently implicit or unavailable.
            selector.InputBehaviorAssignments.Clear();
            selector.InputBehaviorAssignments.Add(new InputBehaviorAssignment
            {
                FlowBlock = page,
                Behavior = suffix == "Description" 
                    ? InputBehavior.First 
                    : InputBehavior.Cross
            });
            var definition = new FlowBloxTestDefinition { Name = "TestDef_" + suffix };
            definition.Entries.Add(new FlowBlockTestDataset { FlowBlock = page });
            definition.Entries.Add(new FlowBlockTestDataset { FlowBlock = selector });
            selector.TestDefinitions.Add(definition);
            definition.OnAfterLoad();
            project.FlowBloxRegistry.RegisterManagedObject(definition);
            writer.ReferencedFlowBlocks.Add(selector);
        }

        VerifyRoundTrip(project, deleteUpstream 
            ? [page, links] 
            : [page]);
    }

    private static void VerifyRoundTrip(FlowBloxProject project, params BaseFlowBlock[] sources)
    {
        var registry = project.FlowBloxRegistry;
        var blocks = registry.GetFlowBlocks().ToList();
        var objects = registry.GetManagedObjects().ToList();
        var components = blocks.Cast<object>().Concat(objects).ToList();
        var before = components.Select(Snapshot).ToList();
        var report = FlowBloxDeletionHelper.Analyze(registry, sources.Cast<object>());
        var dependencies = Describe(report);
        var action = FlowBloxDeletionHelper.CreateAction(registry, report);
        var history = new ProjectChangelist();
        action.Invoke();
        history.AddChange(action);
        var deletedState = components.Select(Snapshot).ToList();
        var retainedBlocks = blocks.Except(sources).Cast<object>().ToList();
        var deletedReport = Describe(FlowBloxDeletionHelper.Analyze(registry, retainedBlocks));

        for (var repetition = 0; repetition < 3; repetition++)
        {
            history.Undo();
            CollectionAssert.AreEquivalent(blocks, registry.GetFlowBlocks().ToList());
            CollectionAssert.AreEquivalent(objects, registry.GetManagedObjects().ToList());
            CollectionAssert.AreEqual(dependencies, Describe(FlowBloxDeletionHelper.Analyze(registry, sources.Cast<object>())), "Deletion dependencies differ after undo.");
            for (var index = 0; index < components.Count; index++)
            {
                var after = Snapshot(components[index]);
                var paths = ((JContainer)before[index]).DescendantsAndSelf().OfType<JValue>()
                    .Concat(((JContainer)after).DescendantsAndSelf().OfType<JValue>())
                    .Select(value => value.Path).Distinct()
                    .Where(path => !JToken.DeepEquals(before[index].SelectToken(path, false), after.SelectToken(path, false)));
                Assert.IsTrue(JToken.DeepEquals(before[index], after), $"Configuration differs after undo: {components[index].GetType().Name} {components[index]}: {string.Join(", ", paths)}");
            }
            history.Redo();
            Assert.IsFalse(registry.GetFlowBlocks().Intersect(sources).Any());
            for (var index = 0; index < components.Count; index++)
                Assert.IsTrue(JToken.DeepEquals(deletedState[index], Snapshot(components[index])), $"Configuration differs after redo: {components[index].GetType().Name} {components[index]}");
            CollectionAssert.AreEqual(deletedReport, Describe(FlowBloxDeletionHelper.Analyze(registry, retainedBlocks)));
        }
    }

    private static JToken Snapshot(object component)
        => JToken.Parse(JsonConvert.SerializeObject(component, JsonSettings.ProjectExport()));

    private static string[] Describe(DeletionReport report)
        => report.Dependencies.Select(dependency =>
            $"{dependency.EditTarget}|{dependency.ReferencingObject.GetType().Name}|{dependency.ReferencingObject}|{dependency.ComponentProperty?.Name}|{dependency.Property.Name}|{dependency.ReferencedObject}|{dependency.Impact}|{dependency.ReviewStatus}|{dependency.CollectionIndex}")
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static T Add<T>(FlowBloxProject project, T block) where T : BaseFlowBlock
    {
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(block);
        project.FlowBloxRegistry.RegisterFlowBlock(block);
        return block;
    }
}
