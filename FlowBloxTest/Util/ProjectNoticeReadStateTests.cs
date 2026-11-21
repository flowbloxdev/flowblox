using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.Projects;

namespace FlowBloxTest.Util;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectNoticeReadStateTests
{
    [TestMethod]
    public void NoticeChange_ReappearsAndReplacesOnlyItsOwnReceipt()
    {
        var first = new FlowBloxProject { Notice = "## Setup\nInstall the component." };
        var second = new FlowBloxProject { Notice = "Another notice" };
        var receipts = ProjectNoticeReadState.MarkRead(first, string.Empty);
        receipts = ProjectNoticeReadState.MarkRead(second, receipts);

        first.Notice += "\nRestart afterwards.";

        Assert.IsFalse(ProjectNoticeReadState.IsRead(first, receipts));
        Assert.IsTrue(ProjectNoticeReadState.IsRead(second, receipts));
        receipts = ProjectNoticeReadState.MarkRead(first, receipts);
        Assert.IsTrue(ProjectNoticeReadState.IsRead(first, receipts));
        Assert.IsTrue(ProjectNoticeReadState.IsRead(second, receipts));
        Assert.AreEqual(2, System.Text.Json.JsonDocument.Parse(receipts).RootElement.EnumerateObject().Count());
    }

    [TestMethod]
    public void ProjectSpaceIdentity_SurvivesLocalRenameButSeparatesEndpoints()
    {
        var project = new FlowBloxProject
        {
            ProjectSpaceGuid = Guid.NewGuid().ToString("N"),
            ProjectSpaceEndpointUri = "https://example.test/api/",
            Notice = "Setup guidance\r\nSecond line"
        };
        var receipts = ProjectNoticeReadState.MarkRead(project, string.Empty);

        project.ProjectGuid = Guid.NewGuid();
        project.ProjectName = "Renamed local copy";
        project.Notice = "Setup guidance\nSecond line\n";
        project.ProjectSpaceEndpointUri = "https://example.test/api";
        Assert.IsTrue(ProjectNoticeReadState.IsRead(project, receipts));

        project.ProjectSpaceEndpointUri = "https://another.example.test/api";
        Assert.IsFalse(ProjectNoticeReadState.IsRead(project, receipts));
    }

    [TestMethod]
    public void InvalidEditableReceipts_DoNotPreventOpeningOrAcknowledgingANotice()
    {
        var project = new FlowBloxProject { Notice = "Read me" };

        Assert.IsFalse(ProjectNoticeReadState.IsRead(project, "invalid JSON"));
        var receipts = ProjectNoticeReadState.MarkRead(project, "invalid JSON");
        Assert.IsTrue(ProjectNoticeReadState.IsRead(project, receipts));
        Assert.IsFalse(ProjectNoticeReadState.IsRead(new FlowBloxProject { Notice = "Read me" }, receipts));
    }
}
