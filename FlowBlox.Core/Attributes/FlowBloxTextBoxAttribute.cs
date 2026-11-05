namespace FlowBlox.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class FlowBloxTextBoxAttribute : Attribute
    {
        public bool MultiLine { get; set; }

        /// <summary>
        /// Masks the value in generated property views. The backing property remains a string so
        /// callers can decide how the value is persisted.
        /// </summary>
        public bool IsPassword { get; set; }

        /// <summary>
        /// Parameterless instance method returning whether the editor masks its value.
        /// </summary>
        public string IsPasswordMethod { get; set; }

        public bool IsCodingMode { get; set; }

        /// <summary>
        /// Optional syntax highlighting definition used by the AvalonEdit code editor.
        /// <para>
        /// This can be either:
        /// </para>
        /// <list type="bullet">
        ///   <item>
        ///     <description>The name of a built-in highlighting definition (e.g., <c>"XML"</c>, <c>"C#"</c>, <c>"JavaScript"</c>).</description>
        ///   </item>
        ///   <item>
        ///     <description>The full resource name of an embedded <c>.xshd</c> file (e.g., <c>"FlowBlox.UICore.Resources.Highlighting.SQL.xshd"</c>).</description>
        ///   </item>
        /// </list>
        /// <para>
        /// Only applicable when <see cref="IsCodingMode"/> is set to <c>true</c>.
        /// </para>
        /// </summary>
        public string SyntaxHighlighting { get; set; }

        /// <summary>
        /// Optional instance property supplying the syntax definition name or resource path.
        /// Overrides SyntaxHighlighting; an empty value disables highlighting.
        /// Changes are reflected in an open editor through INotifyPropertyChanged.
        /// </summary>
        public string SyntaxHighlightingMember { get; set; }

        public FlowBloxTextBoxAttribute()
        {
            MultiLine = false;
            IsPassword = false;
            IsCodingMode = false;
        }
    }
}
