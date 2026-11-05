using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Models.Components
{
    public static class FlowBloxToolboxCategory
    {
        static FlowBloxToolboxCategory()
        {
            InvokeRegistration();
        }

        public static readonly FlowBloxToolboxCategoryItem Regex = new(
            nameof(Regex),
            "FlowBloxToolboxCategory_Regex",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem XPath = new(
            nameof(XPath),
            "FlowBloxToolboxCategory_XPath",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem JPath = new(
            nameof(JPath),
            "FlowBloxToolboxCategory_JPath",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem CounterFormat = new(
            nameof(CounterFormat),
            "FlowBloxToolboxCategory_CounterFormat",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem Format = new(
            nameof(Format),
            "FlowBloxToolboxCategory_Format",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem CompositeFormat = new(
            nameof(CompositeFormat),
            "FlowBloxToolboxCategory_CompositeFormat",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem SQL = new(
            nameof(SQL),
            "FlowBloxToolboxCategory_SQL",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem Filter = new(
            nameof(Filter),
            "FlowBloxToolboxCategory_Filter",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem DBConnection = new(
            nameof(DBConnection),
            "FlowBloxToolboxCategory_DBConnection",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem ChatTemplates = new(
            nameof(ChatTemplates),
            "FlowBloxToolboxCategory_ChatTemplates",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem AIPromptTemplates = new(
            nameof(AIPromptTemplates),
            "FlowBloxToolboxCategory_AIPromptTemplates",
            typeof(FlowBloxTexts));

        public static readonly FlowBloxToolboxCategoryItem ShellExecution = new(
            nameof(ShellExecution),
            "FlowBloxToolboxCategory_ShellExecution",
            typeof(FlowBloxTexts));

        private static readonly Dictionary<string, FlowBloxToolboxCategoryItem> _registry = new(StringComparer.Ordinal);

        internal static void InvokeRegistration()
        {
            var toolboxCacheDirectory = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsToolboxCacheDir)?.Value;
            if (string.IsNullOrWhiteSpace(toolboxCacheDirectory))
                throw new InvalidOperationException("Required option 'Paths.ToolboxCacheDir' is missing.");

            if (Directory.Exists(toolboxCacheDirectory))
                Directory.Delete(toolboxCacheDirectory, true);

            foreach (var service in FlowBloxServiceLocator.Instance.GetServices<IFlowBlockToolboxRegistrationService>())
            {
                service.Register();
            }
        }

        public static void Register(FlowBloxToolboxCategoryItem toolboxCategory)
        {
            if (toolboxCategory == null)
                throw new ArgumentNullException(nameof(toolboxCategory));

            _registry[toolboxCategory.Name] = toolboxCategory;
        }

        public static IReadOnlyCollection<FlowBloxToolboxCategoryItem> GetAllCategories()
        {
            return _registry.Values
                .OrderBy(x => x.Name)
                .ToList();
        }

        public static FlowBloxToolboxCategoryItem GetCategoryOrDefault(string categoryName)
        {
            if (!string.IsNullOrWhiteSpace(categoryName) && _registry.TryGetValue(categoryName, out var category))
                return category;

            return new FlowBloxToolboxCategoryItem(categoryName ?? string.Empty);
        }

        public static string GetDisplayName(string categoryName)
        {
            return GetCategoryOrDefault(categoryName).GetDisplayName();
        }
    }
}
