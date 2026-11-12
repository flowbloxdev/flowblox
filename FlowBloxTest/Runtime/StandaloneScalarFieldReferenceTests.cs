using System.Security.Cryptography;
using System.Text;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.Components.Modifier;
using FlowBlox.Core.Models.FlowBlocks.AWS;
using FlowBlox.Core.Models.FlowBlocks.Generation;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.Runtime;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class StandaloneScalarFieldReferenceTests
{
    private FlowBloxProject _project = null!;
    private IDisposable _scope = null!;

    [TestInitialize]
    public void Initialize()
    {
        _project = new FlowBloxProject();
        _scope = FlowBloxRegistryProvider.BeginScopedRegistry(_project.FlowBloxRegistry);
        var start = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<StartFlowBlock>();
        _project.FlowBloxRegistry.PostProcessFlowBlockCreated(start);
        _project.FlowBloxRegistry.RegisterFlowBlock(start);
    }

    [TestCleanup]
    public void Cleanup() => _scope.Dispose();

    [TestMethod]
    public void TextModifiers_ResolveScalarSelectionsWithoutAnInvocationScope()
    {
        var index = Field("Index", "2");
        var count = Field("Count", "2");
        var substring = new SubstringModifier { Index = 0, Length = 1 };
        substring.SetPropertySelectedField(nameof(substring.Index), index);
        substring.SetPropertySelectedField(nameof(substring.Length), count);
        Assert.AreEqual("cd", substring.Modify(new TransientRuntime(_project), "abcdef"));
        Assert.AreEqual(0, substring.Index);
        Assert.AreEqual(1, substring.Length);
        index.StringValue = "1";
        count.StringValue = "3";
        Assert.AreEqual("bcd", substring.Modify(new TransientRuntime(_project), "abcdef"));

        var lines = new LineExtractorModifier { StartLine = 0, LineCount = null };
        lines.SetPropertySelectedField(nameof(lines.StartLine), Field("LineStart", "1"));
        lines.SetPropertySelectedField(nameof(lines.LineCount), Field("LineCount", "1"));
        Assert.AreEqual("second", lines.Modify(new TransientRuntime(_project), "first\nsecond\nthird"));
        Assert.IsNull(lines.LineCount);
    }

    [TestMethod]
    public void RegexAndReplaceModifiers_ResolveInlineTextAndSelectedBehavior()
    {
        var search = Field("Search", "[0-9]+");
        var replacement = Field("Replacement", "X");
        var behavior = Field("Behavior", "First");
        var match = new RegexMatchModifier { Search = search.FullyQualifiedName, Behavior = RegexMatchModifierBehavior.All };
        match.SetPropertySelectedField(nameof(match.Behavior), behavior);
        var runtime = new TransientRuntime(_project);
        Assert.AreEqual("12", match.Modify(runtime, "12 34"));
        Assert.AreEqual(RegexMatchModifierBehavior.All, match.Behavior);

        var regex = new RegexReplaceModifier { Search = search.FullyQualifiedName, Replace = replacement.FullyQualifiedName };
        Assert.AreEqual("X X", regex.Modify(runtime, "12 34"));
        search.StringValue = "hello";
        var replace = new ReplaceModifier { Search = search.FullyQualifiedName, Replace = replacement.FullyQualifiedName };
        Assert.AreEqual("X world", replace.Modify(runtime, "hello world"));
        Assert.AreEqual(search.FullyQualifiedName, regex.Search);
        Assert.AreEqual(replacement.FullyQualifiedName, replace.Replace);
    }

    [TestMethod]
    public void HttpModifier_ResolvesModeAndDirectionForEveryCall()
    {
        var mode = Field("Mode", "URL");
        var direction = Field("Direction", "Decode");
        var modifier = new HttpEncodeDecodeModifier { Mode = HttpEncodeMode.HTML, Direction = Direction.Encode };
        modifier.SetPropertySelectedField(nameof(modifier.Mode), mode);
        modifier.SetPropertySelectedField(nameof(modifier.Direction), direction);
        var runtime = new TransientRuntime(_project);
        Assert.AreEqual("a b", modifier.Modify(runtime, "a%20b"));
        mode.StringValue = "HTML";
        Assert.AreEqual("&", modifier.Modify(runtime, "&amp;"));
        Assert.AreEqual(Direction.Encode, modifier.Direction);
    }

    [TestMethod]
    public void CryptoModifiers_UseSelectedAlgorithmAndEncodingsWhileKeepingLiterals()
    {
        var algorithm = Field("Algorithm", "SHA512");
        var encoding = Field("OutputEncoding", "Base64");
        var hash = new HashModifier();
        hash.SetPropertySelectedField(nameof(hash.Algorithm), algorithm);
        hash.SetPropertySelectedField(nameof(hash.OutputEncoding), encoding);
        var runtime = new TransientRuntime(_project);
        var bytes = Encoding.UTF8.GetBytes("message");
        Assert.AreEqual(Convert.ToBase64String(SHA512.HashData(bytes)), hash.Modify(runtime, "message"));
        Assert.AreEqual(CryptographicHashAlgorithm.SHA256, hash.Algorithm);

        var hmac = new HmacModifier { SecretKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("secret")) };
        hmac.SetPropertySelectedField(nameof(hmac.Algorithm), algorithm);
        hmac.SetPropertySelectedField(nameof(hmac.OutputEncoding), encoding);
        hmac.SetPropertySelectedField(nameof(hmac.KeyEncoding), Field("KeyEncoding", "Base64"));
        Assert.AreEqual(Convert.ToBase64String(HMACSHA512.HashData(Encoding.UTF8.GetBytes("secret"), bytes)), hmac.Modify(runtime, "message"));
        Assert.AreEqual(CryptographicKeyEncoding.Text, hmac.KeyEncoding);
    }

    [TestMethod]
    public void Base64Modifier_UsesSelectedEncodingWithoutOverwritingTheLiteral()
    {
        var modifier = new Base64ToTextModifier { EncodingName = DotNetEncodingNames.UTF8 };
        modifier.SetPropertySelectedField(nameof(modifier.EncodingName), Field("Encoding", "UTF16"));
        var value = Convert.ToBase64String(Encoding.Unicode.GetBytes("Äpfel"));
        Assert.AreEqual("Äpfel", modifier.Modify(new TransientRuntime(_project), value));
        Assert.AreEqual(DotNetEncodingNames.UTF8, modifier.EncodingName);
    }

    [TestMethod]
    public void ValidationAndSeparatorResolution_ReadScalarFieldsOutsideInvocation()
    {
        var separator = new SplitSeparatorDefinition();
        separator.SetPropertySelectedField(nameof(separator.SpecialSeparator), Field("Separator", "Tab"));
        var context = new ValidationContext(separator) { MemberName = nameof(separator.SpecialSeparator) };
        Assert.AreEqual(ValidationResult.Success, SplitSeparatorDefinition.ValidateSeparator(null!, context));
        Assert.AreEqual("\t", separator.ResolveSeparator());
        Assert.IsNull(separator.SpecialSeparator);

        var counter = new CounterFlowBlock { CounterType = CounterFlowBlock.CounterTypes.Integer };
        counter.SetPropertySelectedField(nameof(counter.CounterType), Field("CounterType", "Alphanumeric"));
        context = new ValidationContext(counter) { MemberName = nameof(counter.StartValue) };
        Assert.AreEqual(ValidationResult.Success, CounterFlowBlock.ValidateStartValue("A", context));
        Assert.AreEqual(CounterFlowBlock.CounterTypes.Integer, counter.CounterType);
    }

    [TestMethod]
    public void Index_ResolvesInitialValueBeforeInvocationAndOnRuntimeRestart()
    {
        var initial = Field("Initial", "42");
        var block = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<IndexFlowBlock>();
        _project.FlowBloxRegistry.PostProcessFlowBlockCreated(block);
        _project.FlowBloxRegistry.RegisterFlowBlock(block);
        block.InitialIndex = 1;
        block.SetPropertySelectedField(nameof(block.InitialIndex), initial);
        var runtime = new TransientRuntime(_project);
        block.RuntimeStarted(runtime);
        initial.StringValue = "43";
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.AreEqual("43", block.GridElementResult.Results.Last().FieldValueMappings.Single().Value);
        initial.StringValue = "99";
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.AreEqual("44", block.GridElementResult.Results.Last().FieldValueMappings.Single().Value);
        initial.StringValue = "7";
        block.RuntimeStarted(runtime);
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.AreEqual("7", block.GridElementResult.Results.Last().FieldValueMappings.Single().Value);
        Assert.AreEqual(1, block.InitialIndex);
    }

    [TestMethod]
    public void StandaloneProviders_ResolveConnectionConfigurationWithoutChangingStoredValues()
    {
        var sftp = new SftpConnectionProvider
        {
            Host = "localhost",
            UserName = "user",
            Password = "secret",
            Port = 22,
            AuthenticationMethod = SftpAuthenticationMethod.PrivateKey
        };
        sftp.SetPropertySelectedField(nameof(sftp.Port), Field("Port", "2222"));
        sftp.SetPropertySelectedField(nameof(sftp.AuthenticationMethod), Field("Authentication", "Password"));
        using var client = sftp.CreateClient();
        Assert.AreEqual(2222, client.ConnectionInfo.Port);
        Assert.AreEqual(22, sftp.Port);

        var aws = new AmazonWebServicesProvider { Region = "eu-central-1", S3ForcePathStyle = false };
        aws.SetPropertySelectedField(nameof(aws.S3ForcePathStyle), Field("PathStyle", "true"));
        Assert.IsTrue(aws.CreateS3Configuration().ForcePathStyle);
        Assert.IsFalse(aws.S3ForcePathStyle);
    }

    private FieldElement Field(string name, string value)
    {
        var field = _project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, name);
        field.StringValue = value;
        return field;
    }
}
