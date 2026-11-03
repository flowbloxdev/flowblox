using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlowBlox.AvaloniaDesktop.Persistence.RecentProjects
{
    public sealed class RecentProjectEntry
    {
        public string ProjectName { get; set; }
        public string ProjectFilePath { get; set; }
        public string ProjectSpaceGuid { get; set; }
        public DateTime LastOpenedUtc { get; set; }
    }
}
