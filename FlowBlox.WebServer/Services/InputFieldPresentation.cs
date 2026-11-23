namespace FlowBlox.WebServer.Services;

public static class InputFieldPresentation
{
    public static string GetDisplayName(string name) =>
        name.Length > 6 && name.StartsWith("Input_", StringComparison.OrdinalIgnoreCase)
            ? name[6..]
            : name;
}
