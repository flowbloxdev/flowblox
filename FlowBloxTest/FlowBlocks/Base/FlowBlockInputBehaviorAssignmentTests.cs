using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.DeepCopier;
using Newtonsoft.Json;
using FlowBloxTest.FlowBlocks.Execution;
using System.Collections.ObjectModel;

namespace FlowBloxTest.FlowBlocks.Base
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class FlowBlockInputBehaviorAssignmentTests : FlowBloxTestsBase
    {
        [TestInitialize]
        public void Initialize()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        }

        [TestMethod]
        public void SingleReference_DefaultsToCross()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var source = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(source);
            target.AssociatedIterationContext = iterationContext;

            var assignment = target.InputBehaviorAssignments.Single();

            Assert.AreSame(source, assignment.FlowBlock);
            Assert.AreEqual(InputBehavior.Cross, assignment.Behavior);
            Assert.IsTrue(target.AutomaticInputBehaviorAssignment);
        }

        [TestMethod]
        public void ReferenceSync_RemovesNullAndUnboundAssignmentsAndPreservesValidBehavior()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var first = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var second = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(first);
            target.AssociatedIterationContext = iterationContext;
            var retained = target.InputBehaviorAssignments.Single();
            retained.Behavior = InputBehavior.Last;
            target.InputBehaviorAssignments.Add(null);
            target.InputBehaviorAssignments.Add(new InputBehaviorAssignment { Behavior = InputBehavior.RowWise });

            target.ReferencedFlowBlocks.Add(second);

            Assert.AreEqual(2, target.InputBehaviorAssignments.Count);
            Assert.AreSame(retained, target.InputBehaviorAssignments.Single(item => item.FlowBlock == first));
            Assert.AreEqual(InputBehavior.Last, retained.Behavior);
            Assert.AreEqual(InputBehavior.First, target.InputBehaviorAssignments.Single(item => item.FlowBlock == second).Behavior);
        }

        [TestMethod]
        public void NoPreviousAssignments_AddingMultipleReferences_SetsAllToFirst()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var firstSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var secondSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>();

            target.ReferencedFlowBlocks = new ObservableCollection<BaseFlowBlock>
            {
                firstSource,
                secondSource
            };

            Assert.AreEqual(2, target.InputBehaviorAssignments.Count);
            Assert.IsTrue(target.InputBehaviorAssignments.All(x => x.Behavior == InputBehavior.First));
        }

        [TestMethod]
        public void AddingSecondReference_ChangesDefaultCrossToFirst()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var firstSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var secondSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(firstSource);
            target.AssociatedIterationContext = iterationContext;
            var existingAssignment = target.InputBehaviorAssignments.Single();

            target.ReferencedFlowBlocks.Add(secondSource);

            Assert.AreSame(existingAssignment, target.InputBehaviorAssignments.Single(x => x.FlowBlock == firstSource));
            Assert.AreEqual(InputBehavior.First, existingAssignment.Behavior);
            Assert.IsTrue(target.AutomaticInputBehaviorAssignment);
            Assert.AreEqual(
                InputBehavior.First,
                target.InputBehaviorAssignments.Single(x => x.FlowBlock == secondSource).Behavior);
        }

        [TestMethod]
        public void RemovingReferencesUntilOneRemains_ChangesBehaviorToCross()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var firstSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var secondSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>();
            target.ReferencedFlowBlocks = new ObservableCollection<BaseFlowBlock>
            {
                firstSource,
                secondSource
            };
            target.AssociatedIterationContext = iterationContext;

            target.ReferencedFlowBlocks.Remove(secondSource);

            var assignment = target.InputBehaviorAssignments.Single();
            Assert.AreSame(firstSource, assignment.FlowBlock);
            Assert.AreEqual(InputBehavior.Cross, assignment.Behavior);
        }

        [TestMethod]
        public void UnchangedReferences_DoNotResynchronizeConfiguredBehavior()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var alternativeIterationContext = CreateFlowBlock<StartFlowBlock>();
            var source = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(source);
            target.AssociatedIterationContext = iterationContext;
            target.InputBehaviorAssignments.Single().Behavior = InputBehavior.Last;

            target.AssociatedIterationContext = alternativeIterationContext;

            Assert.AreEqual(InputBehavior.Last, target.InputBehaviorAssignments.Single().Behavior);
        }

        [TestMethod]
        public void ReplacingSingleReference_PreservesNewAssignmentDefault()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var firstSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var secondSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(firstSource);
            target.AssociatedIterationContext = iterationContext;

            target.ReferencedFlowBlocks[0] = secondSource;

            var assignment = target.InputBehaviorAssignments.Single();
            Assert.AreSame(secondSource, assignment.FlowBlock);
            Assert.AreEqual(InputBehavior.First, assignment.Behavior);
        }

        [TestMethod]
        public void AddingSecondReference_PreservesNonDefaultFirstBehavior()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var firstSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var secondSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(firstSource);
            target.AssociatedIterationContext = iterationContext;
            target.InputBehaviorAssignments.Single().Behavior = InputBehavior.Last;

            target.ReferencedFlowBlocks.Add(secondSource);

            Assert.AreEqual(
                InputBehavior.Last,
                target.InputBehaviorAssignments.Single(x => x.FlowBlock == firstSource).Behavior);
            Assert.AreEqual(
                InputBehavior.First,
                target.InputBehaviorAssignments.Single(x => x.FlowBlock == secondSource).Behavior);
        }

        [TestMethod]
        public void RemovingModifiedReference_DoesNotApplyDefaultsToRemainingInput()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var firstSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var secondSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>();
            target.ReferencedFlowBlocks = new ObservableCollection<BaseFlowBlock>
            {
                firstSource,
                secondSource
            };
            target.AssociatedIterationContext = iterationContext;
            target.InputBehaviorAssignments.Single(x => x.FlowBlock == firstSource).Behavior = InputBehavior.Last;

            target.ReferencedFlowBlocks.Remove(firstSource);

            var assignment = target.InputBehaviorAssignments.Single();
            Assert.AreSame(secondSource, assignment.FlowBlock);
            Assert.AreEqual(InputBehavior.First, assignment.Behavior);
        }

        [TestMethod]
        public void RemovingIterationContext_RemovesAllInputBehaviorAssignments()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var source = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(source);
            target.AssociatedIterationContext = iterationContext;
            Assert.AreEqual(1, target.InputBehaviorAssignments.Count);

            target.AssociatedIterationContext = null;

            Assert.IsFalse(target.HasIterationContext);
            Assert.AreEqual(0, target.InputBehaviorAssignments.Count);
        }

        [TestMethod]
        public void ManualChangeBackToFirst_AllowsRuleBasedDefaults()
        {
            var iterationContext = CreateFlowBlock<StartFlowBlock>();
            var firstSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var secondSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(iterationContext);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(firstSource);
            target.AssociatedIterationContext = iterationContext;
            target.ReferencedFlowBlocks.Add(secondSource);
            var assignment = target.InputBehaviorAssignments.Single(x => x.FlowBlock == firstSource);
            assignment.Behavior = InputBehavior.Last;
            assignment.Behavior = InputBehavior.First;

            target.ReferencedFlowBlocks.Remove(secondSource);

            Assert.IsTrue(target.AutomaticInputBehaviorAssignment);
            Assert.AreEqual(InputBehavior.Cross, assignment.Behavior);
        }

        [DataTestMethod]
        [DataRow(InputBehavior.Cross, InputBehavior.First)]
        [DataRow(InputBehavior.CrossValid, InputBehavior.First)]
        public void SingleToMultipleAndBack_RecognizesValidVariantsAndUsesDefaults(InputBehavior cross, InputBehavior first)
        {
            var context = CreateFlowBlock<StartFlowBlock>();
            var source = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var added = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(source);
            target.AssociatedIterationContext = context;
            target.InputBehaviorAssignments.Single().Behavior = cross;

            target.ReferencedFlowBlocks.Add(added);
            Assert.IsTrue(target.InputBehaviorAssignments.All(x => x.Behavior == first));
            target.ReferencedFlowBlocks.Remove(added);
            Assert.AreEqual(InputBehavior.Cross, target.InputBehaviorAssignments.Single().Behavior);
        }

        [DataTestMethod]
        [DataRow(InputBehavior.First, InputBehavior.First)]
        [DataRow(InputBehavior.FirstValid, InputBehavior.FirstValid)]
        [DataRow(InputBehavior.First, InputBehavior.FirstValid)]
        [DataRow(InputBehavior.FirstValid, InputBehavior.First)]
        public void ReducingFirstInputsToOne_UsesCrossIncludingMixedValidVariants(InputBehavior firstBehavior, InputBehavior secondBehavior)
        {
            var context = CreateFlowBlock<StartFlowBlock>();
            var first = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var second = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(first);
            target.AssociatedIterationContext = context;
            target.ReferencedFlowBlocks.Add(second);
            target.InputBehaviorAssignments.Single(x => x.FlowBlock == first).Behavior = firstBehavior;
            target.InputBehaviorAssignments.Single(x => x.FlowBlock == second).Behavior = secondBehavior;

            target.ReferencedFlowBlocks.Remove(second);

            Assert.AreEqual(InputBehavior.Cross, target.InputBehaviorAssignments.Single().Behavior);
        }

        [DataTestMethod]
        [DataRow(InputBehavior.RowWise)]
        [DataRow(InputBehavior.RowWiseValid)]
        public void AddedInputs_InheritUniformRowWiseBehavior(InputBehavior behavior)
        {
            var context = CreateFlowBlock<StartFlowBlock>();
            var first = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var second = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var third = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(first);
            target.AssociatedIterationContext = context;
            target.InputBehaviorAssignments.Single().Behavior = behavior;

            target.ReferencedFlowBlocks.Add(second);
            Assert.IsTrue(target.InputBehaviorAssignments.All(x => x.Behavior == behavior));
            target.ReferencedFlowBlocks.Add(third);
            Assert.IsTrue(target.InputBehaviorAssignments.All(x => x.Behavior == behavior));
            target.ReferencedFlowBlocks.Remove(first);
            target.ReferencedFlowBlocks.Remove(second);
            Assert.AreEqual(behavior, target.InputBehaviorAssignments.Single().Behavior);
        }

        [TestMethod]
        public void MixedRowWiseBehaviors_DoNotOverrideNewInputDefault()
        {
            var context = CreateFlowBlock<StartFlowBlock>();
            var first = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var second = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var third = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(first);
            target.AssociatedIterationContext = context;
            target.ReferencedFlowBlocks.Add(second);
            target.InputBehaviorAssignments.Single(x => x.FlowBlock == first).Behavior = InputBehavior.RowWise;
            target.InputBehaviorAssignments.Single(x => x.FlowBlock == second).Behavior = InputBehavior.Last;

            target.ReferencedFlowBlocks.Add(third);

            Assert.AreEqual(InputBehavior.First, target.InputBehaviorAssignments.Single(x => x.FlowBlock == third).Behavior);
            Assert.AreEqual(InputBehavior.RowWise, target.InputBehaviorAssignments.Single(x => x.FlowBlock == first).Behavior);
            Assert.AreEqual(InputBehavior.Last, target.InputBehaviorAssignments.Single(x => x.FlowBlock == second).Behavior);
        }

        [TestMethod]
        public void AddingInputBeforeExistingCross_DoesNotRewriteConfiguredOrder()
        {
            var context = CreateFlowBlock<StartFlowBlock>();
            var first = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var added = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(first);
            target.AssociatedIterationContext = context;

            target.ReferencedFlowBlocks.Insert(0, added);

            Assert.AreEqual(InputBehavior.Cross, target.InputBehaviorAssignments.Single(x => x.FlowBlock == first).Behavior);
            Assert.AreEqual(InputBehavior.First, target.InputBehaviorAssignments.Single(x => x.FlowBlock == added).Behavior);
        }

        [TestMethod]
        public void DisabledAssignment_PreservesValuesThroughReferenceChanges()
        {
            var context = CreateFlowBlock<StartFlowBlock>();
            var first = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var second = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>(first);
            target.AssociatedIterationContext = context;
            target.AutomaticInputBehaviorAssignment = false;

            target.ReferencedFlowBlocks.Add(second);
            Assert.AreEqual(InputBehavior.Cross, target.InputBehaviorAssignments.Single(x => x.FlowBlock == first).Behavior);
            Assert.AreEqual(InputBehavior.First, target.InputBehaviorAssignments.Single(x => x.FlowBlock == second).Behavior);
            target.ReferencedFlowBlocks.Remove(first);
            Assert.AreEqual(InputBehavior.First, target.InputBehaviorAssignments.Single().Behavior);
        }

        [TestMethod]
        public void DisabledAssignment_NewSingleInputUsesFirst()
        {
            var context = CreateFlowBlock<StartFlowBlock>();
            var source = CreateFlowBlock<ExecutionOrderTestFlowBlock>(context);
            var target = CreateFlowBlock<ExecutionOrderTestFlowBlock>();
            target.AutomaticInputBehaviorAssignment = false;
            target.AssociatedIterationContext = context;

            target.ReferencedFlowBlocks.Add(source);

            Assert.AreEqual(InputBehavior.First, target.InputBehaviorAssignments.Single().Behavior);
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void JsonAndCopy_PreserveAutomaticAssignmentFlag(bool enabled)
        {
            var source = new ExecutionOrderTestFlowBlock { AutomaticInputBehaviorAssignment = enabled };
            source.OnAfterLoad();
            var restored = JsonConvert.DeserializeObject<ExecutionOrderTestFlowBlock>(JsonConvert.SerializeObject(source));
            restored.OnAfterLoad();
            Assert.AreEqual(enabled, restored.AutomaticInputBehaviorAssignment);
            var copy = new ExecutionOrderTestFlowBlock { AutomaticInputBehaviorAssignment = !enabled };
            copy.OnAfterLoad();
            new DynamicDeepCopier { SpecialMode = DeepCopierSpecialMode.Recopy }.Copy(source, copy);
            Assert.AreEqual(enabled, copy.AutomaticInputBehaviorAssignment);
        }

        [TestMethod]
        public void LegacyLoad_EnablesRulesWithoutChangingConfiguredBehaviors()
        {
            var target = JsonConvert.DeserializeObject<ExecutionOrderTestFlowBlock>(
                "{\"InputBehaviorAssignments\":[{\"Behavior\":\"Last\"}]}");
            target.OnAfterLoad();
            Assert.IsTrue(target.AutomaticInputBehaviorAssignment);
            Assert.AreEqual(InputBehavior.Last, target.InputBehaviorAssignments.Single().Behavior);
        }

        [TestMethod]
        public void ReactiveObjectAfterLoad_HandlesNestedSharedObjectsAndCycles()
        {
            var root = new LoadTrackingReactiveObject();
            var child = new LoadTrackingReactiveObject();
            var assignment = new InputBehaviorAssignment();
            root.Child = child;
            child.Child = root;
            root.Items.Add(assignment);
            child.Items.Add(assignment);
            root.OnAfterLoad();
            Assert.AreEqual(1, root.LoadCount);
            Assert.AreEqual(1, child.LoadCount);
            CollectionAssert.AreEqual(new[] { assignment }, root.GetNestedReactiveObjects().OfType<InputBehaviorAssignment>().ToArray());
            child.Items.Add(new InputBehaviorAssignment());
            root.OnAfterLoad();
            Assert.AreEqual(2, root.LoadCount);
            Assert.AreEqual(2, child.LoadCount);
        }

        private class LoadTrackingReactiveObject : FlowBloxReactiveObject
        {
            public LoadTrackingReactiveObject Child { get; set; }
            public ObservableCollection<FlowBloxReactiveObject> Items { get; set; } = new();
            public int LoadCount { get; private set; }

            public override void OnAfterLoad()
            {
                base.OnAfterLoad();
                LoadCount++;
            }
        }
    }
}
