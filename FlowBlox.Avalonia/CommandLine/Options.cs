using CommandLine;

namespace FlowBlox.AvaloniaDesktop.CommandLine
{
    public class Options
    {
        [Option('p', "project", Required = false, HelpText = "Set path to project file.")]
        public string ProjectFile { get; set; }

        [Option('s', "projectSpaceGuid", Required = false, HelpText = "Set ProjectSpace GUID.")]
        public string ProjectSpaceGuid { get; set; }
    }
}