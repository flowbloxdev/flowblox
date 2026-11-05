using FlowBlox.Core.Constants;
using System.Reflection;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Util;
using FlowBlox.Core.Models.Components;

namespace FlowBlox.Core.Services.Base
{
    public abstract class FlowBlockToolboxRegistrationServiceBase : IFlowBlockToolboxRegistrationService
    {
        public abstract IEnumerable<string> GetAllToolboxResourcesInModule();

        public virtual IEnumerable<FlowBloxToolboxCategoryItem> GetAllToolboxCategoriesInModule()
        {
            return Enumerable.Empty<FlowBloxToolboxCategoryItem>();
        }

        public void Register()
        {
            foreach (string resourceName in GetAllToolboxResourcesInModule())
                RegisterFromResource(resourceName);

            foreach (var toolboxCategory in GetAllToolboxCategoriesInModule())
                FlowBloxToolboxCategory.Register(toolboxCategory);
        }

        private static void RegisterFromResource(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName))
                throw new ArgumentNullException(nameof(resourceName));

            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Resource not found: {resourceName}");

            string extension = Path.GetExtension(resourceName);
            string validFileName = IOUtil.GetValidFileName(resourceName.Substring(0, resourceName.Length - extension.Length));
            string fileName = string.Concat(validFileName, extension);

            string targetDirectory = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsToolboxCacheDir)?.Value;
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new InvalidOperationException("Required option 'Paths.ToolboxCacheDir' is missing.");
            Directory.CreateDirectory(targetDirectory);

            string targetPath = Path.Combine(targetDirectory, fileName);

            using var fileStream = File.Create(targetPath);
            stream.CopyTo(fileStream);
        }
    }
}
