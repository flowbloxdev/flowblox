using System.Globalization;
using FlowBlox.Core.Util;

namespace FlowBloxTest.Util;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class CompactCountFormatterTests
{
    [TestMethod]
    [DataRow(0L, "0")]
    [DataRow(99_999L, "99,999")]
    [DataRow(100_000L, "100k")]
    [DataRow(999_999L, "999k")]
    [DataRow(1_000_000L, "1M")]
    [DataRow(1_234_567L, "1.2M")]
    [DataRow(999_999_999L, "999M")]
    [DataRow(1_234_567_890L, "1.2B")]
    public void Format_PreservesSmallCountsAndDoesNotRoundAcrossUnitBoundaries(long count, string expected)
    {
        Assert.AreEqual(expected, CompactCountFormatter.Format(count, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Format_LocalizesFractionalCounts()
    {
        Assert.AreEqual("1,2M", CompactCountFormatter.Format(1_234_567, CultureInfo.GetCultureInfo("de-DE")));
    }
}
