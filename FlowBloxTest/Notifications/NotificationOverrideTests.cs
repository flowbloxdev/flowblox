using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.FlowBlocks.Calculation;
using FlowBlox.Core.Models.FlowBlocks.AIRemote;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.DistributedData;

namespace FlowBloxTest.Notifications
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class NotificationOverrideTests
    {
        [TestMethod]
        public void DailyMailReportExample_LoadsWithNotificationOverride()
        {
            var file = DistributedDataCatalog.ResolvePath("example_project", "Daily-Mail-Report.fbprj");
            Assert.IsNotNull(file);

            var project = FlowBloxProject.FromJsonContents(File.ReadAllText(file), null);

            var prompt = project.FlowBlocks.OfType<AIPromptFlowBlock>().Single(x => x.HasOverriddenNotifications);
            var entry = prompt.OverriddenNotificationEntries.Single();
            Assert.HasCount(1, entry.Overrides);
            Assert.AreEqual(NotificationType.Error, entry.Overrides[(long)AIPromptFlowBlock.AIPromptNotifications.AIPromptExecutionFailed]);
        }

        [TestMethod]
        public void CreateNotification_UsesErrorOverrideForWarningNotification()
        {
            var (flowBlock, runtime) = CreateTestContext();
            using (runtime)
            {
                var warningRaised = false;
                var errorRaised = false;
                flowBlock.OnWarn += (_, _) => warningRaised = true;
                flowBlock.OnError += (_, _) => errorRaised = true;
                flowBlock.OverrideNotificationType(
                    MathFlowBlock.MathFlowBlockNotifications.ExpressionEmpty,
                    NotificationType.Error);

                flowBlock.CreateNotification(runtime, MathFlowBlock.MathFlowBlockNotifications.ExpressionEmpty);

                Assert.IsFalse(warningRaised);
                Assert.IsTrue(errorRaised);
            }
        }

        [TestMethod]
        public void CreateNotification_UsesWarningOverrideForErrorNotification()
        {
            var (flowBlock, runtime) = CreateTestContext();
            using (runtime)
            {
                var warningRaised = false;
                var errorRaised = false;
                flowBlock.OnWarn += (_, _) => warningRaised = true;
                flowBlock.OnError += (_, _) => errorRaised = true;
                flowBlock.OverrideNotificationType(
                    MathFlowBlock.MathFlowBlockNotifications.EvaluationFailed,
                    NotificationType.Warning);

                flowBlock.CreateNotification(runtime, MathFlowBlock.MathFlowBlockNotifications.EvaluationFailed);

                Assert.IsTrue(warningRaised);
                Assert.IsFalse(errorRaised);
            }
        }

        [TestMethod]
        public void CreateNotification_DoesNotRaiseSuppressedNotification()
        {
            var (flowBlock, runtime) = CreateTestContext();
            using (runtime)
            {
                var notificationRaised = false;
                flowBlock.OnWarn += (_, _) => notificationRaised = true;
                flowBlock.OnError += (_, _) => notificationRaised = true;
                flowBlock.OverrideNotificationType(
                    MathFlowBlock.MathFlowBlockNotifications.ExpressionEmpty,
                    NotificationType.None);

                flowBlock.CreateNotification(runtime, MathFlowBlock.MathFlowBlockNotifications.ExpressionEmpty);

                Assert.IsFalse(notificationRaised);
            }
        }

        private static (MathFlowBlock FlowBlock, TransientRuntime Runtime) CreateTestContext()
        {
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var startFlowBlock = project.FlowBloxRegistry.CreateFlowBlockUnregistered<StartFlowBlock>();
            project.FlowBloxRegistry.PostProcessFlowBlockCreated(startFlowBlock);
            project.FlowBloxRegistry.Register(startFlowBlock);
            return (new MathFlowBlock(), new TransientRuntime(project));
        }
    }
}
