using System.Net;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Projects;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectSpaceContentCacheTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize() => _directory = Path.Combine(Path.GetTempPath(), "FlowBloxProjectCacheTests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task CacheOnlyMiss_FailsWithoutServerAccess()
    {
        using var handler = new ProjectSpaceCacheHttpHandler();
        using var api = new ProjectSpaceCacheWebApi(handler);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new ProjectSpaceContentCache(_directory)
            .LoadAsync(handler.ProjectSpaceGuid, null, "", api, cacheOnly: true));
        Assert.AreEqual(0, handler.MetadataCalls + handler.ContentCalls);
    }

    [TestMethod]
    public async Task UnchangedProject_ValidatesOnce_AndCacheOnlyReusesAcrossCacheInstances()
    {
        using var handler = new ProjectSpaceCacheHttpHandler();
        using var api = new ProjectSpaceCacheWebApi(handler);
        var initial = await new ProjectSpaceContentCache(_directory).LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        var unchanged = await new ProjectSpaceContentCache(_directory).LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        var offline = await new ProjectSpaceContentCache(_directory).LoadAsync(handler.ProjectSpaceGuid, null, "", api, cacheOnly: true);
        CollectionAssert.AreEqual(initial, unchanged);
        CollectionAssert.AreEqual(initial, offline);
        Assert.AreEqual(2, handler.MetadataCalls);
        Assert.AreEqual(1, handler.ContentCalls);
    }

    [TestMethod]
    public async Task ChangedTimestamp_DownloadsReplacement()
    {
        using var handler = new ProjectSpaceCacheHttpHandler();
        using var api = new ProjectSpaceCacheWebApi(handler);
        var cache = new ProjectSpaceContentCache(_directory);
        var initial = await cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        handler.UpdatedAt = handler.CreatedAt!.Value.AddMinutes(1);
        handler.ProjectName = "Changed project";
        var updated = await cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        CollectionAssert.AreNotEqual(initial, updated);
        Assert.AreEqual(2, handler.ContentCalls);
    }

    [TestMethod]
    public async Task StableVersion_IsSeparateFromLatest_AndIgnoresMetadataChanges()
    {
        using var handler = new ProjectSpaceCacheHttpHandler();
        using var api = new ProjectSpaceCacheWebApi(handler);
        var cache = new ProjectSpaceContentCache(_directory);
        var stable = await cache.LoadAsync(handler.ProjectSpaceGuid, 1, "", api);
        handler.UpdatedAt = handler.CreatedAt!.Value.AddDays(5);
        handler.ProjectName = "Changed latest";
        var stableAgain = await cache.LoadAsync(handler.ProjectSpaceGuid, 1, "", api);
        var latest = await cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        CollectionAssert.AreEqual(stable, stableAgain);
        CollectionAssert.AreNotEqual(stable, latest);
        Assert.AreEqual(2, handler.ContentCalls);
        Assert.AreEqual(2, Directory.GetFiles(_directory, "project.json", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task UnverifiableTimestamps_DoNotSilentlyReuseOldContent()
    {
        using var handler = new ProjectSpaceCacheHttpHandler { CreatedAt = null };
        using var api = new ProjectSpaceCacheWebApi(handler);
        var cache = new ProjectSpaceContentCache(_directory);
        await cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        await cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        Assert.AreEqual(2, handler.ContentCalls);
    }

    [TestMethod]
    public async Task CorruptCache_FailsOffline_AndRepairsThroughNormalDownload()
    {
        using var handler = new ProjectSpaceCacheHttpHandler();
        using var api = new ProjectSpaceCacheWebApi(handler);
        var cache = new ProjectSpaceContentCache(_directory);
        await cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        var file = Directory.GetFiles(_directory, "project.json", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(file, "{broken json");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api, cacheOnly: true));
        Assert.AreEqual(1, handler.MetadataCalls);
        await cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        await cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api, cacheOnly: true);
        Assert.AreEqual(2, handler.ContentCalls);
    }

    [TestMethod]
    public async Task AccessDenied_DoesNotFallBackToPreviouslyCachedContent()
    {
        using var handler = new ProjectSpaceCacheHttpHandler();
        using var api = new ProjectSpaceCacheWebApi(handler);
        var cache = new ProjectSpaceContentCache(_directory);
        await cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api);
        handler.MetadataStatus = HttpStatusCode.Forbidden;
        await Assert.ThrowsExactlyAsync<ProjectSpaceLoginRequiredException>(() => cache.LoadAsync(handler.ProjectSpaceGuid, null, "", api));
        Assert.AreEqual(1, handler.ContentCalls);
    }

    [TestMethod]
    public async Task ConcurrentLoads_DownloadOnlyOnce()
    {
        using var handler = new ProjectSpaceCacheHttpHandler();
        using var api = new ProjectSpaceCacheWebApi(handler);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => new ProjectSpaceContentCache(_directory)
            .LoadAsync(handler.ProjectSpaceGuid, null, "", api)));
        Assert.AreEqual(1, handler.ContentCalls);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task ProjectLoading_CreatesFreshInstances_FromTheSharedCachedDefinition()
    {
        var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsLocalAppDataDir);
        var previous = option.PersistentValue;
        using var handler = new ProjectSpaceCacheHttpHandler();
        using var api = new ProjectSpaceCacheWebApi(handler);
        try
        {
            option.PersistentValue = _directory;
            var first = await FlowBloxProject.FromProjectSpaceGuidAsync(handler.ProjectSpaceGuid, "", api);
            first.ProjectName = "Runtime change";
            var second = await FlowBloxProject.FromProjectSpaceGuidAsync(handler.ProjectSpaceGuid, "", api, cacheOnly: true);
            Assert.AreEqual(handler.ProjectName, second.ProjectName);
            Assert.AreEqual(first.ProjectGuid, second.ProjectGuid);
            Assert.AreNotSame(first.FlowBloxRegistry, second.FlowBloxRegistry);
            Assert.AreEqual(handler.ProjectSpaceGuid, second.ProjectSpaceGuid);
            Assert.AreEqual(1, handler.MetadataCalls);
            Assert.AreEqual(1, handler.ContentCalls);
        }
        finally
        {
            option.PersistentValue = previous;
        }
    }
}
