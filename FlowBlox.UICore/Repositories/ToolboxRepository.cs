using FlowBlox.Core.Constants;
using System.IO;
using System.Text.Json;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Util;
using FlowBlox.UICore.Models.Toolbox;

namespace FlowBlox.UICore.Repositories
{
    public class ToolboxRepository
    {
        private readonly string _toolboxUserFile;
        private readonly string _toolboxDirectory;
        private readonly string _toolboxCacheDirectory;

        public ToolboxRepository(FlowBloxOptions options)
        {
            _toolboxUserFile = options.GetOption(OptionsConstants.PathsToolboxUserFile)?.Value;
            _toolboxDirectory = options.GetOption(OptionsConstants.PathsToolboxDir)?.Value;
            _toolboxCacheDirectory = options.GetOption(OptionsConstants.PathsToolboxCacheDir)?.Value;

            if (string.IsNullOrWhiteSpace(_toolboxUserFile))
                throw new InvalidOperationException("Toolbox user file is not set.");

            if (string.IsNullOrWhiteSpace(_toolboxDirectory))
                throw new InvalidOperationException("Toolbox directory is not set.");

            if (string.IsNullOrWhiteSpace(_toolboxCacheDirectory))
                throw new InvalidOperationException("Toolbox cache directory option 'Paths.ToolboxCacheDir' is not set.");
        }

        public IEnumerable<ToolboxElement> Query(string? toolboxCategory = null, string toolboxFile = null, ToolboxScope? toolboxScope = null)
        {
            if (!Directory.Exists(_toolboxDirectory))
                Directory.CreateDirectory(_toolboxDirectory);

            var files = Directory.GetFiles(_toolboxDirectory, "*.json").ToList();
            if (!files.Contains(_toolboxUserFile))
                files.Add(_toolboxUserFile);

            var globalFiles = Directory.GetFiles(_toolboxCacheDirectory, "*.json");
            files.AddRange(globalFiles);

            IEnumerable<string> filteredFiles = files;

            if (toolboxScope != null)
            {
                if (toolboxScope == ToolboxScope.User)
                    filteredFiles = filteredFiles.Where(x => x.ToLower() == _toolboxUserFile.ToLower());
                else if (toolboxScope == ToolboxScope.ToolboxDirectory)
                    filteredFiles = filteredFiles.Where(x => x.ToLower().IndexOf(_toolboxDirectory.ToLower()) == 0);
            }
            else if (!string.IsNullOrEmpty(toolboxFile))
                filteredFiles = filteredFiles.Where(x => x.ToLower() == toolboxFile.ToLower());

            var elements = new List<ToolboxElement>();
            foreach (var file in filteredFiles)
            {
                if (!File.Exists(file))
                    continue;

                var content = File.ReadAllText(file);
                var summary = JsonSerializer.Deserialize<ToolboxElementSummary>(content);
                foreach (var element in summary.ToolboxElements)
                {
                    element.UnderlyingToolboxFile = file;
                    element.IsEditable = file == _toolboxUserFile;
                    if (toolboxCategory == null || element.ToolboxCategory == toolboxCategory)
                        elements.Add(element);
                }
            }
            return elements
                .OrderBy(x => x.ToolboxCategory.ToString())
                .ThenBy(x => x.Name);
        }

        public void Update(ToolboxElement element)
        {
            var elements = Query(toolboxFile: element.UnderlyingToolboxFile).ToList();
            var existingElement = elements.FirstOrDefault(e => e.Name == element.Name);
            if (existingElement != null)
                elements.Remove(existingElement);

            elements.Add(element);

            SaveToFile(elements, element.UnderlyingToolboxFile);
        }

        public void Delete(ToolboxElement element)
        {
            var elements = Query(toolboxFile: element.UnderlyingToolboxFile).ToList();
            var elementToRemove = elements.FirstOrDefault(e => e.Name == element.Name);
            if (elementToRemove != null)
                elements.Remove(elementToRemove);

            SaveToFile(elements, element.UnderlyingToolboxFile);
        }

        private void SaveToFile(List<ToolboxElement> elements, string file)
        {
            var summary = new ToolboxElementSummary { ToolboxElements = elements };
            var content = JsonSerializer.Serialize(summary);

            var directory = Path.GetDirectoryName(file);

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(file, content);
        }

        internal ToolboxElement Create()
        {
            var toolboxElement = new ToolboxElement();
            toolboxElement.UnderlyingToolboxFile = _toolboxUserFile;
            toolboxElement.IsEditable = true;
            return toolboxElement;
        }   
    }
}
