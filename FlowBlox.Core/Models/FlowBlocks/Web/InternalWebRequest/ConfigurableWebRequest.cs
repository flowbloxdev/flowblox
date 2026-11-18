using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.Components.Authorization;
using FlowBlox.Core.Validations;

namespace FlowBlox.Core.Models.FlowBlocks.Web.InternalWebRequest
{
    public class ConfigurableWebRequest : IDisposable
    {
        private const string DefaultHTTPAction = "GET";

        private bool _hasStartedRequest = false;

        public string Url { get; set; }
        public string HttpAction { get; set; }
        public int Timeout { get; set; }
        public string UserAgent { get; set; }
        public string Accept { get; set; }
        public string AcceptEncoding { get; set; }
        public bool KeepAlive { get; set; }
        public bool ExpectContinue { get; set; }
        public string Version { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        [CustomValidation(typeof(OAuthConnectionValidator), nameof(OAuthConnectionValidator.Validate))]
        public OAuthUserConnection OAuthConnection { get; set; }
        public CancellationToken CancellationToken { get; set; }
        public string ContentType { get; set; }
        public IDictionary<string, string> Headers { get; set; }
        public string Payload { get; set; }
        public ResponseBodyKind ResponseBodyKind { get; set; }

        public HttpClient Client { get; }

        public HttpClientHandler Handler { get; }

        public ConfigurableWebRequest()
        {
            Handler = new HttpClientHandler()
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
                {
                    return sslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
                }
            };
            Client = new HttpClient(Handler);
        }

        public ConfigurableWebRequestResult Invoke(ConfigurableWebRequest webRequest)
        {
            return Task.Run(async () => await InvokeAsync()).GetAwaiter().GetResult();
        }

        public async Task<ConfigurableWebRequestResult> InvokeAsync()
        {
            if (!_hasStartedRequest)
            {
                SetupServicePointManager();
                Client.Timeout = TimeSpan.FromMilliseconds(Timeout);
                _hasStartedRequest = true;
            }

            var method = new HttpMethod(HttpAction ?? DefaultHTTPAction);
            using var requestMessage = new HttpRequestMessage(method, Url);

            requestMessage.Headers.ConnectionClose = !KeepAlive;
            requestMessage.Headers.ExpectContinue = ExpectContinue;

            if (!System.Version.TryParse(Version, out var version))
                throw new InvalidOperationException($"Unable to parse HTTP version \"{version}\".");

            requestMessage.Version = version;

            if (!string.IsNullOrEmpty(UserAgent))
                requestMessage.Headers.UserAgent.ParseAdd(UserAgent);

            if (!string.IsNullOrEmpty(AcceptEncoding))
                requestMessage.Headers.AcceptEncoding.ParseAdd(AcceptEncoding);

            if (!string.IsNullOrEmpty(UserName) &&
                !string.IsNullOrEmpty(Password))
            {
                var byteArray = Encoding.ASCII.GetBytes($"{UserName}:{Password}");
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            }

            if (OAuthConnection != null)
            {
                var accessToken = await OAuthConnection.GetAccessTokenAsync(new Uri(Url), CancellationToken).ConfigureAwait(false);
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            foreach (var header in Headers ?? new Dictionary<string, string>())
            {
                requestMessage.Headers.Add(header.Key, header.Value);
            }

            if (!string.IsNullOrEmpty(Payload))
            {
                requestMessage.Content = new StringContent(Payload, Encoding.UTF8, ContentType);
            }

            using HttpResponseMessage response = await Client.SendAsync(requestMessage, CancellationToken).ConfigureAwait(false);
            var headers = response.Content.Headers;
            if (ResponseBodyKind == ResponseBodyKind.Bytes)
            {
                byte[] data = await response.Content.ReadAsByteArrayAsync();
                return new ConfigurableWebRequestResult
                {
                    Success = response.IsSuccessStatusCode,
                    UrlCalled = response.RequestMessage.RequestUri.ToString(),
                    BodyKind = ResponseBodyKind.Bytes,
                    Bytes = data,
                    FileName = ResolveFileName(response, response.RequestMessage.RequestUri)
                };
            }
            else
            {
                var encoding = GetEncodingFromContentType(headers);
                using (var responseStream = await response.Content.ReadAsStreamAsync())
                using (var streamReader = new StreamReader(responseStream, encoding, detectEncodingFromByteOrderMarks: true))
                {
                    string content = await streamReader.ReadToEndAsync();
                    return new ConfigurableWebRequestResult
                    {
                        Success = response.IsSuccessStatusCode,
                        UrlCalled = response.RequestMessage.RequestUri.ToString(),
                        BodyKind = ResponseBodyKind.Text,
                        Content = content
                    };
                }
            }
        }

        private static string ResolveFileName(HttpResponseMessage resp, Uri requestUri)
        {
            var contentDisposition = resp.Content.Headers.ContentDisposition;
            var name = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
            if (!string.IsNullOrEmpty(name))
                return name.Trim('\"');

            var last = Path.GetFileName(requestUri.LocalPath);
            if (!string.IsNullOrEmpty(last))
                return last;

            throw new InvalidOperationException("No filename could be determined.");
        }

        private static Encoding GetEncodingFromContentType(HttpContentHeaders headers)
        {
            if (headers != null &&
                headers.ContentType != null &&
                headers.ContentType.CharSet != null)
            {
                try
                {
                    return Encoding.GetEncoding(headers.ContentType.CharSet);
                }
                catch (ArgumentException)
                {
                    // Fallback to UTF-8 if encoding is not recognized
                    return Encoding.UTF8;
                }
            }
            // Default to UTF-8 if no charset is specified
            return Encoding.UTF8;
        }

        private void SetupServicePointManager()
        {
            ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls |
                    SecurityProtocolType.Tls11 |
                    SecurityProtocolType.Tls12 |
                    SecurityProtocolType.Tls13;
        }

        public void Dispose()
        {
            Client.Dispose();
        }
    }
}
