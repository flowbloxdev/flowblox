using FlowBlox.Core.Util.DeepCopier;

namespace FlowBloxTest.Util;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class DictionaryDeepCopyTests
{
    [TestMethod]
    public void CopyAndRecopy_IsolateGenericDictionariesAndPreserveComparersAndEntryReferences()
    {
        var value = new object();
        var original = new DictionaryCopySettings
        {
            Labels = new Dictionary<int, string> { [1] = "Original" },
            References = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["Entry"] = value }
        };
        var copier = new DynamicDeepCopier();

        var copy = (DictionaryCopySettings)copier.Copy(original);
        copy.Labels![1] = "Edited";
        copy.References!["Additional"] = value;

        Assert.AreEqual("Original", original.Labels[1]);
        Assert.IsFalse(original.References.ContainsKey("Additional"));
        Assert.AreSame(original.References.Comparer, copy.References.Comparer);
        Assert.AreSame(value, copy.References["ENTRY"]);

        copier.SpecialMode = DeepCopierSpecialMode.Recopy;
        Assert.AreSame(original, copier.Copy(copy));
        Assert.AreEqual("Edited", original.Labels[1]);
        Assert.AreNotSame(copy.Labels, original.Labels);
        Assert.AreNotSame(copy.References, original.References);
        Assert.AreSame(value, original.References["ADDITIONAL"]);

        copy.Labels[1] = "Uncommitted";
        Assert.AreEqual("Edited", original.Labels[1]);
    }

    [TestMethod]
    public void Recopy_CanClearDictionaryWithoutMutatingTheOriginalDuringEditing()
    {
        var original = new DictionaryCopySettings
        {
            Labels = new Dictionary<int, string> { [1] = "Original" }
        };
        var copier = new DynamicDeepCopier();
        var copy = (DictionaryCopySettings)copier.Copy(original);
        Assert.IsNull(copy.References);
        copy.Labels = null;
        Assert.IsNotNull(original.Labels);

        copier.SpecialMode = DeepCopierSpecialMode.Recopy;
        copier.Copy(copy);

        Assert.IsNull(original.Labels);
    }
}
