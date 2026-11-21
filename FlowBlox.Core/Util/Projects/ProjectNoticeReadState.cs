using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowBlox.Core.Models.Project;

namespace FlowBlox.Core.Util.Projects;

/// <summary>
/// Remembers one acknowledged notice revision per project without storing its contents.
/// </summary>
public static class ProjectNoticeReadState
{
    public static bool IsRead(FlowBloxProject project, string storedReceipts)
    {
        return ReadReceipts(storedReceipts).TryGetValue(GetIdentity(project), out var hash)
            && hash == GetNoticeHash(project);
    }

    public static string MarkRead(FlowBloxProject project, string storedReceipts)
    {
        var receipts = ReadReceipts(storedReceipts);
        receipts[GetIdentity(project)] = GetNoticeHash(project);
        return JsonSerializer.Serialize(receipts);
    }

    private static Dictionary<string, string> ReadReceipts(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(value) ?? new();
        }
        catch (JsonException)
        {
            // An invalid editable option must never prevent a project from opening.
            return new();
        }
    }

    private static string GetIdentity(FlowBloxProject project)
    {
        return string.IsNullOrWhiteSpace(project.ProjectSpaceGuid)
            ? "local:" + project.ProjectGuid.ToString("N")
            : "ps:" + project.ProjectSpaceEndpointUri?.TrimEnd('/').ToLowerInvariant()
                + ":" + project.ProjectSpaceGuid.ToLowerInvariant();
    }

    private static string GetNoticeHash(FlowBloxProject project)
    {
        var notice = (project.Notice ?? string.Empty).Replace("\r\n", "\n").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notice)));
    }
}
