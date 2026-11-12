using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FlowBloxTest.Services
{
    internal sealed class MediaDownloadTestServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(15));
        private readonly Task _serving;

        public string Url { get; }

        public MediaDownloadTestServer(byte[] body, bool truncate = false)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/download.bin";
            _serving = ServeAsync(body, truncate);
        }

        private async Task ServeAsync(byte[] body, bool truncate)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token)))
                {
                }

                var length = body.Length + (truncate ? 100 : 0);
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, _stop.Token);
                await stream.WriteAsync(body, _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _serving.GetAwaiter().GetResult();
            _listener.Stop();
            _stop.Dispose();
        }
    }
}
