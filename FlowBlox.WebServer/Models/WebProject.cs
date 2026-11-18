using FlowBlox.Core.Models.Project.Launcher;

namespace FlowBlox.WebServer.Models;

public sealed record WebProject(string Id, ProjectShortcut Shortcut, string Group);
