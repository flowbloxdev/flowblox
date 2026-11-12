using FlowBlox.Core.Authentication;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Xml;

namespace FlowBlox.Core.Models.Components
{
    public class OptionElement
    {
        public enum OptionType
        {
            Text,
            Password,
            Integer,
            Boolean
        };

        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public OptionType Type { get; set; }
        public bool SystemOption { get; internal set; }
        public bool IsPlaceholderEnabled { get; set; } = true;

        public OptionElement()
        {
            this.Type = OptionType.Text;
        }

        private string _internalValue { get; set; }

        internal string StorageToken => _internalValue;

        internal bool HasStoredValue => !string.IsNullOrWhiteSpace(_internalValue);

        /// <summary>
        /// Gets the persistent value with option placeholders and environment variables resolved.
        /// Setting this property updates <see cref="PersistentValue"/>.
        /// </summary>
        [JsonIgnore]
        public string Value
        {
            get => FlowBloxOptions.ResolveOptionValue(this);
            set => PersistentValue = value;
        }


        /// <summary>
        /// Gets or sets the persistent value without resolving option placeholders or environment variables.
        /// </summary>
        [JsonProperty("Value")]
        public string PersistentValue
        {
            get => GetPersistentValue();
            set
            {
                if (Type == OptionType.Password)
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        this._internalValue = string.Empty;
                    }
                    else
                    {
                        var encryptedData = FlowBloxSecureStorageManager.SetProtectedData(Name, value);
                        this._internalValue = encryptedData;
                    }
                }
                else
                {
                    this._internalValue = value;
                }
            }
        }

        private string GetPersistentValue()
        {
            if (Type == OptionType.Password)
            {
                return string.IsNullOrEmpty(_internalValue)
                    ? string.Empty
                    : FlowBloxSecureStorageManager.GetProtectedData(Name, _internalValue);
            }

            return _internalValue ?? string.Empty;
        }

        public OptionElement(string name, string value, string description, OptionType type, string displayName = null, bool isPlaceholderEnabled = false)
        {
            this.Name = name;
            this.DisplayName = displayName;
            this._internalValue = value;
            this.Description = description;
            this.Type = type;
            this.SystemOption = true;
            this.IsPlaceholderEnabled = isPlaceholderEnabled;
        }

        public void Validate()
        {
            if (this.Type == OptionType.Integer)
            {
                if (!int.TryParse(Value, out _))
                {
                    throw new ValidationException($"The value '{Value}' of the option '{Name}' is not a valid integer.");
                }
            }
            else if (this.Type == OptionType.Boolean)
            {
                if (!bool.TryParse(Value, out _))
                {
                    throw new ValidationException($"The value '{Value}' of the option '{Name}' is not a valid boolean.");
                }
            }
        }

        public int GetValueInt()
        {
            if (int.TryParse(Value, out var value))
            {
                return value;
            }

            return 0;
        }

        public bool GetValueBoolean()
        {
            if (bool.TryParse(Value, out var value))
            {
                return value;
            }

            return false;
        }

        internal static OptionElement FromXml(XmlNode xmlNode)
        {
            OptionElement optionElement = new OptionElement();

            string name = xmlNode.Attributes["name"].InnerText;
            string displayName = xmlNode.Attributes["displayName"].InnerText;
            string value = xmlNode.Attributes["value"].InnerText;
            string description = xmlNode.Attributes["desc"].InnerText;
            OptionType type = (OptionType)Enum.Parse(typeof(OptionType), xmlNode.Attributes["type"].InnerText);

            bool system = xmlNode.Attributes["system"].InnerText.Equals("True");

            optionElement.Name = name;
            optionElement.DisplayName = displayName;
            optionElement._internalValue = value;
            optionElement.Description = description;
            optionElement.Type = type;
            optionElement.SystemOption = system;

            // Backward-compatible default:
            // - if attribute exists: honor it
            // - if missing: default true for non-system options, false for system options
            var showAttr = xmlNode.Attributes["isPlaceholderEnabled"];
            if (showAttr != null && bool.TryParse(showAttr.InnerText, out var show))
            {
                optionElement.IsPlaceholderEnabled = show;
            }
            else
            {
                optionElement.IsPlaceholderEnabled = !system;
            }

            return optionElement;
        }

        internal XmlNode SaveXml(XmlDocument xmlDocument)
        {
            XmlNode xnOptionElement = xmlDocument.CreateElement("option_element");

            XmlAttribute xaName = xmlDocument.CreateAttribute("name");
            XmlAttribute xaDisplayName = xmlDocument.CreateAttribute("displayName");
            XmlAttribute xaValue = xmlDocument.CreateAttribute("value");
            XmlAttribute xaDesc = xmlDocument.CreateAttribute("desc");
            XmlAttribute xaType = xmlDocument.CreateAttribute("type");
            XmlAttribute xaSystem = xmlDocument.CreateAttribute("system");

            XmlAttribute xaShowInFieldSelectionDialog = xmlDocument.CreateAttribute("isPlaceholderEnabled");

            xaName.InnerText = Name;
            xaDisplayName.InnerText = DisplayName;
            xaValue.InnerText = _internalValue;
            xaDesc.InnerText = Description;
            xaType.InnerText = Type.ToString();
            xaSystem.InnerText = SystemOption.ToString();

            xaShowInFieldSelectionDialog.InnerText = IsPlaceholderEnabled.ToString();

            xnOptionElement.Attributes.Append(xaName);
            xnOptionElement.Attributes.Append(xaDisplayName);
            xnOptionElement.Attributes.Append(xaValue);
            xnOptionElement.Attributes.Append(xaDesc);
            xnOptionElement.Attributes.Append(xaType);
            xnOptionElement.Attributes.Append(xaSystem);
            xnOptionElement.Attributes.Append(xaShowInFieldSelectionDialog);

            return xnOptionElement;
        }
    }
}
