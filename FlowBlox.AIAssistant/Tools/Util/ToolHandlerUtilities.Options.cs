using System.Globalization;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static partial class ToolHandlerUtilities
    {
        public const string PasswordOptionProtectedValueMessage =
            "This option stores an encrypted value. The AI assistant cannot read or write it.";

        private static Func<AssistantConfiguration>? _assistantConfigurationProvider;

        private static readonly HashSet<string> ImmutableOptionNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "AI.AssistantConfiguration",
            AuxiliaryProjectApprovalStore.OptionName,
            "AI.CommuncationProtocolDir",
            "AI.AssistantHistoryDirectory",
            "Account.LicenseToken",
            "Aspose.Words.LicenseBase64",
            "SQL.AllowDestructiveStatements"
        };

        private static readonly string[] ImmutableOptionPrefixes =
        {
            "Account.",
            "Api.",
            "Editor.",
            "EPPlus.",
            "FieldView.",
            "Grid.",
            "MainPanel.",
            "Notifications.",
            "Paths.",
            "PopUp.",
            "PropertyWindow.",
            "Runtime.Debugging.",
            "Runtime.Window.",
            "TestDefinitionView.",
            "UI.",
            "Updates."
        };

        public static void SetAssistantConfigurationProvider(Func<AssistantConfiguration>? configurationProvider)
        {
            _assistantConfigurationProvider = configurationProvider;
        }

        public static void EnsureOptionsReadAccess()
        {
            var accessLevel = GetOptionsAccessLevel();
            if (accessLevel == AssistantOptionsAccessLevel.None)
                throw new InvalidOperationException(GetOptionsPermissionDeniedMessage("search options"));
        }

        public static void EnsureOptionsWriteAccess()
        {
            var accessLevel = GetOptionsAccessLevel();
            if (accessLevel != AssistantOptionsAccessLevel.ReadWrite)
                throw new InvalidOperationException(GetOptionsPermissionDeniedMessage("set options"));
        }

        public static ToolResponse SearchOptions(JObject args)
        {
            var normalizedTerms = ParseContainsSearchTerms(
                args["searchForNames"] ?? args["SearchForNames"]);

            var candidates = FlowBloxOptions.GetOptionInstance()
                .GetOptions()
                .Where(IsAvailableToAssistant);

            var filtered = normalizedTerms.Count == 0
                ? candidates
                : candidates.Where(option => normalizedTerms.Any(term => OptionMatches(option, term)));

            var optionElements = filtered
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToOptionInfo);

            return Ok(new JObject
            {
                ["searchForNames"] = new JArray(normalizedTerms),
                ["matchMode"] = "CaseInsensitiveContainsOR",
                ["options"] = new JArray(optionElements)
            });
        }

        public static ToolResponse SetOptionValue(JObject args)
        {
            var key = args.Value<string>("key")
                      ?? args.Value<string>("name")
                      ?? args.Value<string>("optionName");
            var rawValue = args.Value<string>("stringValue")
                           ?? args.Value<string>("value");

            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Option key is required.");

            if (rawValue == null)
                throw new InvalidOperationException("stringValue is required.");

            var options = FlowBloxOptions.GetOptionInstance();
            var option = options.GetOption(key.Trim());
            if (option == null)
                throw new InvalidOperationException($"Option '{key}' was not found.");

            if (!IsAvailableToAssistant(option))
                throw new InvalidOperationException($"Option '{option.Name}' is not available to the AI assistant.");

            if (option.Type == OptionElement.OptionType.Password)
                throw new InvalidOperationException(
                    $"Option '{option.Name}' stores an encrypted value. The AI assistant cannot read or write password options.");

            var convertedValue = ConvertOptionStringValue(option, rawValue);
            option.Value = convertedValue;
            options.Save();

            return Ok(new JObject
            {
                ["updated"] = true,
                ["option"] = ToOptionInfo(option)
            });
        }

        private static AssistantOptionsAccessLevel GetOptionsAccessLevel()
        {
            var configuration = _assistantConfigurationProvider?.Invoke();
            if (configuration != null)
                return configuration.OptionsAccessLevel;

            var rawConfig = FlowBloxOptions.GetOptionInstance()
                .GetOption("AI.AssistantConfiguration")?
                .Value ?? string.Empty;

            var parseResult = AssistantConfigurationJson.Parse(rawConfig);
            return parseResult.Configuration?.OptionsAccessLevel ?? AssistantOptionsAccessLevel.None;
        }

        private static string GetOptionsPermissionDeniedMessage(string operation)
        {
            return
                $"The AI assistant is not currently permitted to {operation}. " +
                "Enable this in AI Assistant Configuration > Permissions > Options access.";
        }

        private static bool OptionMatches(OptionElement option, string term)
        {
            return ContainsIgnoreCase(option.Name, term)
                   || ContainsIgnoreCase(option.DisplayName, term)
                   || ContainsIgnoreCase(option.Description, term)
                   || (option.Type != OptionElement.OptionType.Password && ContainsIgnoreCase(option.Value, term));
        }

        private static bool IsAvailableToAssistant(OptionElement option)
        {
            if (option == null || option.Type == OptionElement.OptionType.Password)
                return false;
            if (ImmutableOptionNames.Contains(option.Name ?? string.Empty))
                return false;

            return !ImmutableOptionPrefixes.Any(prefix =>
                (option.Name ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsIgnoreCase(string? value, string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return true;

            return (value ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static JObject ToOptionInfo(OptionElement option)
        {
            var isPassword = option.Type == OptionElement.OptionType.Password;
            return new JObject
            {
                ["name"] = option.Name,
                ["displayName"] = option.DisplayName ?? string.Empty,
                ["description"] = option.Description ?? string.Empty,
                ["type"] = option.Type.ToString(),
                ["value"] = isPassword ? PasswordOptionProtectedValueMessage : option.Value,
                ["isPlaceholderEnabled"] = option.IsPlaceholderEnabled,
                ["systemOption"] = option.SystemOption
            };
        }

        private static string ConvertOptionStringValue(OptionElement option, string rawValue)
        {
            switch (option.Type)
            {
                case OptionElement.OptionType.Integer:
                    if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                    {
                        throw new InvalidOperationException(
                            $"Option '{option.Name}' expects an integer value, but received '{rawValue}'.");
                    }

                    return intValue.ToString(CultureInfo.InvariantCulture);

                case OptionElement.OptionType.Boolean:
                    if (!bool.TryParse(rawValue, out var boolValue))
                    {
                        throw new InvalidOperationException(
                            $"Option '{option.Name}' expects a boolean value. Use 'true' or 'false'.");
                    }

                    return boolValue ? "true" : "false";

                case OptionElement.OptionType.Password:
                    throw new InvalidOperationException(
                        $"Option '{option.Name}' stores an encrypted value. The AI assistant cannot read or write password options.");

                case OptionElement.OptionType.Text:
                default:
                    return rawValue;
            }
        }
    }
}
