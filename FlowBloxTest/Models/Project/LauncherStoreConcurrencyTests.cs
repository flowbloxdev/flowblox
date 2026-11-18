using FlowBlox.Core.Models.Project.Launcher;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class LauncherStoreConcurrencyTests
{
    [TestMethod]
    public void IndependentStoreInstances_DoNotLoseConcurrentUpdates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxStores-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "entries.json");
            Parallel.For(0, 30, index => new LauncherJsonStore<int>(path).Update(entries => entries.Add(index)));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 30).ToArray(), new LauncherJsonStore<int>(path).Read());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
