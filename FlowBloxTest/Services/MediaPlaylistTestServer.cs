using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FlowBloxTest.Services
{
    internal sealed class MediaPlaylistTestServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(90));
        private readonly Task _serving;
        private readonly byte[] _audio = CreateAudio();

        public string Url { get; }

        public MediaPlaylistTestServer()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/playlist.html";
            _serving = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var requestLine = await reader.ReadLineAsync(_stop.Token);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token)))
                    {
                    }

                    var path = requestLine?.Split(' ')[1];
                    var isPlaylist = path == "/playlist.html";
                    var contentType = isPlaylist ? "text/html; charset=utf-8" : "audio/wav";
                    var body = isPlaylist
                        ? Encoding.UTF8.GetBytes("<html><head><title>Playlist</title></head><body>" +
                            "<video src='/first.wav' controls></video>" +
                            "<video src='/broken.wav' controls></video>" +
                            "<video src='/last.wav' controls></video></body></html>")
                        : path == "/broken.wav" ? Encoding.ASCII.GetBytes("Invalid media") : _audio;
                    var header = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _stop.Token);
                    if (requestLine?.StartsWith("HEAD ", StringComparison.Ordinal) != true)
                        await stream.WriteAsync(body, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private static byte[] CreateAudio()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            const int sampleBytes = 1600;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + sampleBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(8000);
            writer.Write(16000);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(sampleBytes);
            writer.Write(new byte[sampleBytes]);
            writer.Flush();
            return stream.ToArray();
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
