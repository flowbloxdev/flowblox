using System.ComponentModel.DataAnnotations;
using System.Net;
using FlowBlox.Core.Authentication.OAuth;
using FlowBlox.Core.Models.Components.Authorization;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.FlowBlocks.Web.InternalWebRequest;
using FlowBlox.Core.Models.FlowBlocks.Web.OpenApi;

namespace FlowBloxTest.Authorization;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class OAuthUserConnectionTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxOAuthTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public async Task BrowserCallback_RejectsWrongStateBeforeAcceptingTheMatchingAuthorization()
    {
        using var session = new OAuthAuthorizationSession(CreateSettings());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pending = session.WaitForCodeAsync(timeout.Token);
        using var client = new HttpClient();
        using var rejected = await client.GetAsync(session.RedirectUri + "?state=wrong&code=untrusted", timeout.Token);
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.IsFalse(pending.IsCompleted);
        var query = System.Web.HttpUtility.ParseQueryString(session.AuthorizationUri.Query);
        Assert.AreEqual("S256", query["code_challenge_method"]);
        Assert.IsFalse(string.IsNullOrEmpty(query["code_challenge"]));
        using var accepted = await client.GetAsync(session.RedirectUri + "?state=" + Uri.EscapeDataString(query["state"]!) + "&code=accepted-code", timeout.Token);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        Assert.AreEqual("accepted-code", await pending);
    }

    [TestMethod]
    public async Task BrowserCallback_StopsWaitingWhenExecutionIsCancelled()
    {
        using var session = new OAuthAuthorizationSession(CreateSettings());
        using var cancellation = new CancellationTokenSource();
        var pending = session.WaitForCodeAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
    }

    [TestMethod]
    public async Task ConcurrentExpiredRequests_RefreshOnceAndPersistRotatedCredentials()
    {
        var settings = CreateSettings();
        var store = new OAuthTokenStore(_directory);
        store.Write(settings.StorageKey, new OAuthTokenSet
        {
            AccessToken = "expired-access", RefreshToken = "initial-refresh", ExpiresUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        var calls = 0;
        var handler = new OAuthTestHttpHandler
        {
            Send = async (request, cancellation) =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(50, cancellation);
                var form = await request.Content!.ReadAsStringAsync(cancellation);
                Assert.Contains("grant_type=refresh_token", form);
                Assert.Contains("initial-refresh", form);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"fresh-access\",\"refresh_token\":\"rotated-refresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
                };
            }
        };
        using var client = new HttpClient(handler);
        var service = new OAuthTokenService(store, client);
        var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => service.GetAccessTokenAsync(settings, new Uri(settings.ResourceBaseUrl))));
        Assert.IsTrue(tokens.All(token => token == "fresh-access"));
        Assert.AreEqual(1, calls);
        Assert.AreEqual("rotated-refresh", store.Read(settings.StorageKey)!.RefreshToken);
        var stored = File.ReadAllText(Directory.GetFiles(_directory, "*.credential").Single());
        Assert.DoesNotContain("fresh-access", stored);
        Assert.DoesNotContain("rotated-refresh", stored);
    }

    [TestMethod]
    public async Task MissingCredentials_RequireInteractionWithoutSendingATokenRequest()
    {
        var handler = new OAuthTestHttpHandler { Send = (_, _) => throw new AssertFailedException("Unexpected token request.") };
        using var client = new HttpClient(handler);
        var service = new OAuthTokenService(new OAuthTokenStore(_directory), client);
        var settings = CreateSettings();
        await Assert.ThrowsAsync<OAuthInteractionRequiredException>(() => service.GetAccessTokenAsync(settings, new Uri(settings.ResourceBaseUrl)));
    }

    [TestMethod]
    public async Task RevokedRefreshToken_RemovesUnusableCredentialsWithoutLeakingProviderDetails()
    {
        var store = new OAuthTokenStore(_directory);
        var settings = CreateSettings();
        store.Write(settings.StorageKey, new OAuthTokenSet { RefreshToken = "old-refresh" });
        var handler = new OAuthTestHttpHandler
        {
            Send = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"private-provider-detail\"}")
            })
        };
        using var client = new HttpClient(handler);
        var service = new OAuthTokenService(store, client);
        var error = await Assert.ThrowsAsync<OAuthInteractionRequiredException>(() => service.GetAccessTokenAsync(settings, new Uri(settings.ResourceBaseUrl)));
        Assert.IsNull(store.Read(settings.StorageKey));
        Assert.DoesNotContain("private-provider-detail", error.Message);
    }

    [TestMethod]
    public void ResourceScope_RejectsOtherHostsAndSiblingPaths()
    {
        var settings = CreateSettings();
        settings.ValidateResource(new Uri("https://api.example.test/v1/messages"));
        Assert.ThrowsExactly<ValidationException>(() => settings.ValidateResource(new Uri("https://other.example.test/v1/messages")));
        Assert.ThrowsExactly<ValidationException>(() => settings.ValidateResource(new Uri("https://api.example.test/v10/messages")));
    }

    [TestMethod]
    public void WebRequestAnnotation_RejectsAnAuthorizationHeaderAlongsideTheConnection()
    {
        var block = new WebRequestFlowBlock { OAuthConnection = new OAuthUserConnection() };
        block.HeaderParameters.Add(new WebRequestParameter { Key = "authorization", Value = "Bearer manual" });
        var errors = new List<ValidationResult>();
        Assert.IsFalse(Validator.TryValidateProperty(block.OAuthConnection,
            new ValidationContext(block) { MemberName = nameof(block.OAuthConnection) }, errors));
        Assert.HasCount(1, errors);
    }

    [TestMethod]
    public void RestAnnotation_RejectsABearerTokenAlongsideTheConnection()
    {
        var block = new OpenApiRestFlowBlock { OAuthConnection = new OAuthUserConnection(), BearerToken = "manual-token" };
        Assert.ThrowsExactly<ValidationException>(() => Validator.ValidateProperty(block.OAuthConnection,
            new ValidationContext(block) { MemberName = nameof(block.OAuthConnection) }));
    }

    [TestMethod]
    public void WebRequestAnnotation_AllowsUnrelatedHeadersWithTheConnection()
    {
        var block = new WebRequestFlowBlock { OAuthConnection = new OAuthUserConnection() };
        block.HeaderParameters.Add(new WebRequestParameter { Key = "Accept", Value = "application/json" });
        Validator.ValidateProperty(block.OAuthConnection,
            new ValidationContext(block) { MemberName = nameof(block.OAuthConnection) });
    }

    [TestMethod]
    public void ConnectionSettings_ResolveOptionsWithoutReplacingConfiguredReferences()
    {
        var key = "OAuthTest." + Guid.NewGuid().ToString("N");
        var options = FlowBlox.Core.Util.FlowBloxOptions.GetOptionInstance().OptionCollection;
        options.Add(key, new FlowBlox.Core.Models.Components.OptionElement(
            key, "https://auth.example.test/authorize", "Test option", FlowBlox.Core.Models.Components.OptionElement.OptionType.Text,
            isPlaceholderEnabled: true));
        try
        {
            var connection = new OAuthUserConnection
            {
                AuthorizationEndpoint = "$Options::" + key,
                TokenEndpoint = "https://auth.example.test/token",
                ClientId = "desktop",
                ResourceBaseUrl = "https://api.example.test/"
            };
            Assert.AreEqual("https://auth.example.test/authorize", connection.GetSettings().AuthorizationEndpoint);
            Assert.AreEqual("$Options::" + key, connection.AuthorizationEndpoint);
        }
        finally
        {
            options.Remove(key);
        }
    }

    [TestMethod]
    public void ClientSecret_ResolvesInputParameterWithoutRequiringPasswordMetadata()
    {
        var registry = new FlowBlox.Core.Provider.Registry.FlowBloxRegistry();
        using var scope = FlowBlox.Core.Provider.FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var input = registry.CreateUserField(
            FlowBlox.Core.Enums.UserFieldTypes.Input,
            FlowBlox.Core.Enums.FieldTypes.Text,
            "ClientSecret");
        input.StringValue = "entered-secret";
        input.IsPassword = false;
        var connection = new OAuthUserConnection { ClientSecretReference = input.FullyQualifiedName };

        Assert.AreEqual("entered-secret", connection.GetClientSecret());
        Assert.AreEqual(input.FullyQualifiedName, connection.ClientSecretReference);
    }

    [TestMethod]
    public async Task CodeExchange_SendsPkceAndPersistsTheReturnedSession()
    {
        var store = new OAuthTokenStore(_directory);
        var settings = CreateSettings();
        var handler = new OAuthTestHttpHandler
        {
            Send = async (request, cancellation) =>
            {
                var form = await request.Content!.ReadAsStringAsync(cancellation);
                Assert.Contains("grant_type=authorization_code", form);
                Assert.Contains("code_verifier=test-verifier", form);
                Assert.Contains("client_secret=test-client-secret", form);
                Assert.IsFalse(request.RequestUri!.Query.Contains("test-client-secret", StringComparison.Ordinal));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"issued-access\",\"refresh_token\":\"issued-refresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
                };
            }
        };
        using var client = new HttpClient(handler);
        await new OAuthTokenService(store, client).CompleteAuthorizationAsync(
            settings, "test-code", "test-verifier", "http://127.0.0.1:12345/oauth/callback/", "test-client-secret");
        Assert.AreEqual("issued-refresh", store.Read(settings.StorageKey)!.RefreshToken);
    }

    private static OAuthConnectionSettings CreateSettings() => new()
    {
        ConnectionId = Guid.NewGuid(),
        AuthorizationEndpoint = "https://auth.example.test/authorize",
        TokenEndpoint = "https://auth.example.test/token",
        ClientId = "desktop-test-client",
        Scopes = "read",
        ResourceBaseUrl = "https://api.example.test/v1/"
    };
}
