namespace FlowBlox.Core.Attributes
{
    [Flags()]
    public enum UIOperations
    {
        None = 0,
        Create = 1,
        Edit = 2,
        Delete = 4,
        Link = 8,
        Unlink = 16,
        All = 31
    }

    public enum UIFactory
    {
        Default,
        GridView,
        ListView,
        Association,
        ListViewSplitMode,
        ComboBox
    }

    [Flags]
    public enum UIOptions
    {
        /// <summary>
        /// Enables field/placeholder selection for supported property editors.
        /// </summary>
        EnableFieldSelection = 1,

        /// <summary>
        /// Enables file selection button for text-like editors.
        /// </summary>
        EnableFileSelection = 2,

        /// <summary>
        /// Enables folder selection button for text-like editors.
        /// </summary>
        EnableFolderSelection = 4
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class FlowBloxUIAttribute : System.ComponentModel.DataAnnotations.ValidationAttribute
    {
        public FlowBloxUIAttribute()
        {
            this.DisplayLabel = true;
            this.Operations = UIOperations.All;
            this.Visible = true;
            this.CreatableTypes = Array.Empty<Type>();
        }

        public UIOperations Operations { get; set; }

        public bool DisplayLabel { get; set; }

        public bool Visible { get; set; }

        /// <summary>
        /// Selects the editor used for this property; Default lets the UI choose based on its type.
        /// </summary>
        public UIFactory Factory { get; set; }

        /// <summary>
        /// Specifies a factory type implementing IItemFactory&lt;T&gt; with a public parameterless constructor.
        /// The association editor calls Create() to create an instance compatible with the property type.
        /// </summary>
        public Type ItemFactory { get; set; }

        /// <summary>
        /// Specifies the registered toolbox category identifier for selectable text templates, not a display label.
        /// For example, nameof(FlowBloxToolboxCategory.Regex) offers regular-expression templates.
        /// </summary>
        public string ToolboxCategory { get; set; }

        /// <summary>
        /// Names a parameterless instance method returning the selectable items as a list.
        /// Define a public method on the current class, for example nameof(GetAvailableItems); nested objects can use their parent's method.
        /// </summary>
        public string SelectionFilterMethod { get; set; }

        /// <summary>
        /// Names a property on the current class or its parent whose PropertyChanged notification refreshes the selection list.
        /// Use the property name; the owning object must implement INotifyPropertyChanged.
        /// </summary>
        public string SelectionFilterDependency { get; set; }

        /// <summary>
        /// Names a public parameterless method on the current class returning bool.
        /// Returning true makes the property's editor read-only.
        /// </summary>
        public string ReadOnlyMethod { get; set; }

        public UIOptions UiOptions { get; set; }
        
        /// <summary>
        /// Specifies the public member path on each selectable item whose value is shown in the selection control.
        /// For example, "Name" displays each item's Name instead of its default string representation.
        /// </summary>
        public string SelectionDisplayMember { get; set; }
        

        public bool Maximize { get; set; }
        
        public bool ReadOnly { get; set; }

        // <summary>
        /// If set, only these concrete types may be created for abstract/interface property types.
        /// </summary>
        public Type[] CreatableTypes { get; set; }

        protected override System.ComponentModel.DataAnnotations.ValidationResult IsValid(
            object value,
            System.ComponentModel.DataAnnotations.ValidationContext validationContext)
        {
            // Possible unresolved references are runtime warnings, never UI validation failures.
            return System.ComponentModel.DataAnnotations.ValidationResult.Success;
        }
    }
}
