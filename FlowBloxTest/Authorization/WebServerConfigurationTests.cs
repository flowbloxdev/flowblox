using FlowBlox.Core.Authentication;

namespace FlowBloxTest.Authorization;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class WebServerConfigurationTests
{
    [TestMethod]
    public void PasswordVerification_RejectsMalformedAndExcessiveWorkFactors()
    {
        Assert.IsFalse(WebServerPasswordHasher.Verify("password", "1.2147483647.AA==.AA=="));
        Assert.IsFalse(WebServerPasswordHasher.Verify("password", "1.210000.invalid.invalid"));
        Assert.IsFalse(WebServerPasswordHasher.Verify("password", "unknown-format"));
    }
}
