using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Registry;

namespace FlowBloxTest.Provider;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class UserFieldOrderingTests
{
    [TestMethod]
    public void NullableIndices_PlaceUnindexedFieldsFirstAndKeepTiesStable()
    {
        var registry = new FlowBloxRegistry();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var tiedFirst = CreateField(registry, "Zulu", 10);
        var zero = CreateField(registry, "Zero", 0);
        var unindexedFirst = CreateField(registry, "UnindexedZulu");
        var tiedSecond = CreateField(registry, "Alpha", 10);
        var negative = CreateField(registry, "Negative", -1);
        var unindexedSecond = CreateField(registry, "UnindexedAlpha");

        CollectionAssert.AreEqual(
            new[] { unindexedFirst, unindexedSecond, negative, zero, tiedFirst, tiedSecond },
            registry.GetUserFields());
    }

    private static FieldElement CreateField(FlowBloxRegistry registry, string name, int? index = null)
    {
        var field = registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, name);
        field.Index = index;
        return field;
    }
}
