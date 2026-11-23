namespace FlowBlox.WebServer.Models;

public sealed class InputListEntry
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Value { get; set; } = string.Empty;
}
