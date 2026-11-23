using FlowBlox.AIAssistant.History;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Util;
using Newtonsoft.Json;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AiAssistantHistoryIndexTests
{
    private string _directory = null!;
    private string _previousDirectory = null!;
    private readonly AiAssistantHistoryStore _store = AiAssistantHistoryStore.Instance;

    [TestInitialize]
    public void Initialize()
    {
        var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIAssistantHistoryDirectory);
        _previousDirectory = option.Value;
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxHistoryIndexTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        option.Value = _directory;
    }

    [TestCleanup]
    public void Cleanup()
    {
        FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIAssistantHistoryDirectory).Value = _previousDirectory;
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FlowBloxHistoryIndexTests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_directory).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public void UnchangedHistoryCanBeListedWithoutReopeningItsArchive()
    {
        var history = new AiAssistantHistoryDocument { ProjectGuid = Guid.NewGuid(), ComponentScope = "SyntheticComponent" };
        var path = _store.CreateOrUpdateHistory(history, null);
        Assert.AreEqual(history.HistoryGuid, _store.LoadList(history.ProjectGuid).Single().HistoryGuid);

        using var lockedArchive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var cached = _store.LoadList(history.ProjectGuid).Single();
        Assert.AreEqual(history.HistoryGuid, cached.HistoryGuid);
        Assert.AreEqual("SyntheticComponent", cached.ComponentScope);
    }

    [TestMethod]
    public void ExternalChangesAndRemovalRefreshTheIndex()
    {
        var history = new AiAssistantHistoryDocument { ProjectGuid = Guid.NewGuid(), ComponentScope = "Before" };
        var path = Path.Combine(_directory, "history_synthetic.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(history));
        Assert.AreEqual("Before", _store.LoadList(history.ProjectGuid).Single().ComponentScope);

        history.ComponentScope = "After";
        File.WriteAllText(path, JsonConvert.SerializeObject(history));
        Assert.AreEqual("After", _store.LoadList(history.ProjectGuid).Single().ComponentScope);

        File.Delete(path);
        Assert.AreEqual(0, _store.LoadList(history.ProjectGuid).Count);
    }

    [TestMethod]
    public void SavingInvalidatesTheIndexEvenWhenFileMetadataMatches()
    {
        var history = new AiAssistantHistoryDocument { ProjectGuid = Guid.NewGuid(), ComponentScope = "Before" };
        var path = _store.CreateOrUpdateHistory(history, null);
        Assert.AreEqual("Before", _store.LoadList(history.ProjectGuid).Single().ComponentScope);
        var previousTimestamp = File.GetLastWriteTimeUtc(path);

        history.ComponentScope = "After";
        _store.CreateOrUpdateHistory(history, path);
        File.SetLastWriteTimeUtc(path, previousTimestamp);
        Assert.AreEqual("After", _store.LoadList(history.ProjectGuid).Single().ComponentScope);
    }

    [TestMethod]
    public void TemporarilyLockedHistoryIsRetriedAfterTheLockIsReleased()
    {
        var history = new AiAssistantHistoryDocument { ProjectGuid = Guid.NewGuid() };
        var path = Path.Combine(_directory, "history_locked.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(history));

        using (var lockedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.AreEqual(0, _store.LoadList(history.ProjectGuid).Count);

        Assert.AreEqual(history.HistoryGuid, _store.LoadList(history.ProjectGuid).Single().HistoryGuid);
    }
}
