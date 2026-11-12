using FlowBlox.Core.Util.Downloads;

namespace FlowBloxTest.Util.Downloads
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class TransferProgressReporterTests
    {
        [TestMethod]
        public void FrequentStreamReads_DoNotFloodLog_AndCompletionIsReportedOnce()
        {
            var messages = new List<string>();
            var clock = new ManualTimeProvider();
            var reporter = new TransferProgressReporter(messages.Add, clock);
            for (var received = 0; received <= 10000; received++)
                reporter.ReportBytes(received, 10000);

            reporter.Complete();
            reporter.Complete();
            reporter.ReportBytes(10000, 10000);

            Assert.HasCount(3, messages);
            StringAssert.Contains(messages[1], "100%");
            Assert.AreEqual("Transfer completed.", messages[2]);
        }

        [TestMethod]
        public void IntermediateProgress_RequiresBothTimeAndPercentageThreshold()
        {
            var messages = new List<string>();
            var clock = new ManualTimeProvider();
            var reporter = new TransferProgressReporter(messages.Add, clock);
            reporter.Report("Downloading", 0);
            clock.Advance(TimeSpan.FromSeconds(2));
            reporter.Report("Downloading", 0.04);
            Assert.HasCount(1, messages);

            reporter.Report("Downloading", 0.05);
            Assert.HasCount(2, messages);

            clock.Advance(TimeSpan.FromSeconds(1));
            reporter.Report("Downloading", 0.10);
            Assert.HasCount(2, messages);

            clock.Advance(TimeSpan.FromSeconds(1));
            reporter.Report("Downloading", 0.10);
            Assert.HasCount(3, messages);
        }

        [TestMethod]
        public void UnknownLength_ReportsReceivedBytesAtTimeIntervals()
        {
            var messages = new List<string>();
            var clock = new ManualTimeProvider();
            var reporter = new TransferProgressReporter(messages.Add, clock);
            reporter.ReportBytes(1024, null);
            reporter.ReportBytes(2048, null);
            clock.Advance(TimeSpan.FromSeconds(2));
            reporter.ReportBytes(1048576, null);

            Assert.HasCount(2, messages);
            StringAssert.Contains(messages[1], "1.0 MiB");
        }

        [TestMethod]
        public void ProcessPositions_AreThrottledWithoutInventingTotalProgress()
        {
            var messages = new List<string>();
            var clock = new ManualTimeProvider();
            var reporter = new TransferProgressReporter(messages.Add, clock);
            reporter.ReportPosition(TimeSpan.FromSeconds(1));
            for (var index = 0; index < 1000; index++)
                reporter.ReportPosition(TimeSpan.FromSeconds(2));

            Assert.HasCount(1, messages);
            clock.Advance(TimeSpan.FromSeconds(2));
            reporter.ReportPosition(TimeSpan.FromSeconds(3));

            Assert.HasCount(2, messages);
            StringAssert.Contains(messages[1], "00:00:03 processed");
            Assert.IsFalse(messages[1].Contains('%'));
        }

        [TestMethod]
        public void ConversionPhase_IsReportedOnceWithoutInventedPercentages()
        {
            var messages = new List<string>();
            var clock = new ManualTimeProvider();
            var reporter = new TransferProgressReporter(messages.Add, clock);
            reporter.Report("Downloading", 1);
            reporter.Report("Converting media");
            for (var index = 0; index < 100; index++)
            {
                clock.Advance(TimeSpan.FromSeconds(3));
                reporter.Report("Converting media");
            }

            Assert.HasCount(2, messages);
            Assert.AreEqual("Converting media: started.", messages[1]);
        }
    }
}
