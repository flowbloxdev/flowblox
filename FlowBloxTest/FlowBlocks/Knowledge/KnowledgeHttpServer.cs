using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FlowBloxTest.FlowBlocks.Knowledge;

public sealed class KnowledgeHttpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
    private readonly Task _serving;
    private readonly Func<KnowledgeHttpRequest, KnowledgeHttpResponse> _respond;
    public List<KnowledgeHttpRequest> Requests { get; } = [];
    public string BaseUrl { get; }

    public KnowledgeHttpServer(Func<KnowledgeHttpRequest, KnowledgeHttpResponse> respond)
    {
        _respond = respond;
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _serving = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = client.GetStream();
                var requestLine = (await ReadLineAsync(stream)).Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string line;
                while ((line = await ReadLineAsync(stream)).Length > 0)
                {
                    var separator = line.IndexOf(':');
                    headers[line[..separator]] = line[(separator + 1)..].Trim();
                }

                var body = await ReadBodyAsync(stream, headers);
                var request = new KnowledgeHttpRequest { Method = requestLine[0], Path = requestLine[1], Headers = headers, Body = body };
                Requests.Add(request);
                var response = _respond(request);
                var bytes = Encoding.UTF8.GetBytes(response.Body);
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {response.Status} Result\r\nContent-Type: {response.ContentType}; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, _stop.Token);
                await stream.WriteAsync(bytes, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task<string> ReadBodyAsync(NetworkStream stream, Dictionary<string, string> headers)
    {
        using var body = new MemoryStream();
        if (headers.TryGetValue("Transfer-Encoding", out var encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            while (true)
            {
                var size = int.Parse((await ReadLineAsync(stream)).Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (size == 0)
                {
                    await ReadLineAsync(stream);
                    break;
                }

                var chunk = new byte[size];
                await stream.ReadExactlyAsync(chunk, _stop.Token);
                body.Write(chunk);
                await ReadLineAsync(stream);
            }
        }
        else if (headers.TryGetValue("Content-Length", out var length))
        {
            var bytes = new byte[int.Parse(length, CultureInfo.InvariantCulture)];
            await stream.ReadExactlyAsync(bytes, _stop.Token);
            body.Write(bytes);
        }

        return Encoding.UTF8.GetString(body.ToArray());
    }

    private async Task<string> ReadLineAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var current = new byte[1];
        while (true)
        {
            await stream.ReadExactlyAsync(current, _stop.Token);
            if (current[0] == '\n')
                break;

            if (current[0] != '\r')
                bytes.Add(current[0]);
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _serving.GetAwaiter().GetResult();
        _stop.Dispose();
    }
}
