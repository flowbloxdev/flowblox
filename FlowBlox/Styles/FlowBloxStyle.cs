using FlowBlox.Core.Constants;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FlowBlox.Core.Util;

namespace FlowBlox.Styles
{
    /// <summary>
    /// The application's style class. The current style can be defined in the OptionsConstants.UIStyle option.
    /// In this version, styles cannot yet be defined manually.
    /// </summary>
    public class FlowBloxStyle
    {
        // <summary>
        // LEGACY REFERENCE ONLY:
        // Applies a style to a <see cref="FlowBlockUIElement"/>.
        // This method belongs to the previous WinForms-based FlowBlock UI implementation
        // and is retained solely for behavioral comparison during the WPF migration.
        // </summary>
        // <param name="uiElement"></param>
        // internal static void ApplyStyle(FlowBlockUIElement uiElement)
        // {
        //     FlowBloxSyleBase baseStyle = GetStyleFromOptions();
        // 
        //     if (baseStyle != null)
        //     {
        //         uiElement.ForeColor = baseStyle.FlowBlockUIElementForeColor;
        // 
        //         foreach (Control childControl in uiElement.Controls)
        //         {
        //             if (childControl is TableLayoutPanel)
        //             {
        //                 ((TableLayoutPanel)childControl).BackColor = baseStyle.FlowBlockUIElementBackColor;
        //             }
        //         }
        //     }
        // }

        /// <summary>
        /// Applies a style to a <see cref="Form"/> object.
        /// This method should be called after the `InitializeComponent` method.
        /// </summary>
        /// <param name="form"></param>
        public static void ApplyStyle(Form form)
        {
            FlowBloxSyleBase style = GetStyleFromOptions();
            if (style != null)
            {
                form.BackColor = style.ControlBackColor;
                form.ForeColor = style.ControlForeColor;
            }
            if (form.MainMenuStrip != null)
            {
                ApplyStyle(form.MainMenuStrip);
            }

            foreach (Control control in form.Controls)
            {
                ApplyStyle(control);
            }
        }

        /// <summary>
        /// Applies a style to a <see cref="Control"/> object.
        /// </summary>
        /// <param name="control"></param>
        public static void ApplyStyle(Control control)
        {
            if (HasTag(control.Tag, FlowBloxStyleTags.StyleIgnore))
                return;

            if (!HasTag(control.Tag, FlowBloxStyleTags.StyleIgnoreSelf))
            {
                FlowBloxSyleBase style = GetStyleFromOptions();
                if (style != null)
                {
                    if (HasTag(control.Tag, FlowBloxStyleTags.StyleHighlight))
                        control.BackColor = style.ControlHighlightBackColor;
                    else if (HasTag(control.Tag, FlowBloxStyleTags.StyleHighlightHint))
                        control.BackColor = style.ControlHighlightHintBackColor;
                    else if (HasTag(control.Tag, FlowBloxStyleTags.StyleHeader))
                    {
                        control.BackColor = style.ControlHeaderBackColor;
                        control.ForeColor = style.ControlHeaderForeColor;
                    }
                    else
                    {
                        control.BackColor = style.ControlBackColor;
                    }

                    control.ForeColor = style.ControlForeColor;

                    if (!HasTag(control.Tag, FlowBloxStyleTags.StyleKeepFont))
                        control.Font = style.DefaultFont;
                }

                if (control is TextBox) ApplyStyle((TextBox)control);
                if (control is CheckBox) ApplyStyle((CheckBox)control);
                if (control is Button) ApplyStyle((Button)control);
                if (control is MenuStrip) ApplyStyle((MenuStrip)control);
                if (control is ToolStrip) ApplyStyle((ToolStrip)control);
                if (control is ListView) ApplyStyle((ListView)control);
                if (control is DataGridView) ApplyStyle((DataGridView)control);
                if (control.ContextMenuStrip != null) ApplyStyle(control.ContextMenuStrip);
            }

            foreach (Control childControl in control.Controls)
            {
                ApplyStyle(childControl);
            }
        }

        /// <summary>
        /// Returns the currently selected style object based on the configured option.
        /// </summary>
        private static FlowBloxSyleBase GetStyleFromOptions()
        {
            string selectedStyle = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.UIStyle].Value;
            if (selectedStyle.Equals("Professional"))
                return new FlowBloxProfessionalStyle();

            return null;
        }

        /// <summary>
        /// Checks whether a given tag matches a FlowBlox style tag.
        /// Supports comma, semicolon and pipe separated tag lists.
        /// </summary>
        private static bool HasTag(object tag, string expected)
        {
            if (tag == null)
                return false;

            if (tag.Equals(expected))
                return true;

            if (tag is string s)
            {
                var parts = s.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    if (p.Trim().Equals(expected, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        private static void ApplyStyle(TextBox textBox)
        {
            var style = GetStyleFromOptions();
            if (style != null)
            {
                if (!textBox.ReadOnly)
                    textBox.BackColor = style.TextBoxBackColor;
            }
        }

        private static void ApplyStyle(DataGridView dataGridView)
        {
            FlowBloxSyleBase style = GetStyleFromOptions();
            if (style != null)
            {
                dataGridView.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                dataGridView.CellBorderStyle = DataGridViewCellBorderStyle.Single;
                dataGridView.BorderStyle = BorderStyle.None;

                dataGridView.ScrollBars = ScrollBars.Both;
                dataGridView.ColumnHeadersDefaultCellStyle.Font = style.HeaderFont;
                dataGridView.ColumnHeadersDefaultCellStyle.Padding = new Padding(5);

                dataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
                dataGridView.ColumnHeadersHeight = 40;
            }
        }

        private static void ApplyStyle(Button button)
        {
            var flowBloxStyle = GetStyleFromOptions();
            if (flowBloxStyle != null)
            {
                button.FlatStyle = flowBloxStyle.ButtonFlatStyle;
            }
        }

        private static void ApplyStyle(CheckBox checkBox)
        {
            checkBox.Padding = new Padding(5);
            checkBox.Margin = new Padding(5);
        }

        private static void ApplyStyle(ListView listView)
        {
            FlowBloxSyleBase style = GetStyleFromOptions();
            if (style != null)
            {
                listView.BackColor = style.ListViewBackColor;
                listView.OwnerDraw = true;
                listView.FullRowSelect = true;
                listView.GridLines = false;

                typeof(ListView).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(listView, true, null);

                if (!HasTag(listView.Tag, FlowBloxStyleTags.StyleListViewDrawCustom))
                {
                    listView.DrawSubItem += (s, e) => e.DrawDefault = true;
                    listView.DrawItem += (s, e) => e.DrawDefault = true;
                }

                listView.DrawColumnHeader += (s, e) =>
                {
                    e.DrawBackground();
                    using (var headerFont = style.HeaderFont)
                    {
                        TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont, e.Bounds, SystemColors.WindowText,
                            TextFormatFlags.VerticalCenter |
                            TextFormatFlags.LeftAndRightPadding |
                            TextFormatFlags.Left);
                    }
                };
            }
        }

        private static void ApplyStyle(ToolStrip toolStrip)
        {
            FlowBloxSyleBase style = GetStyleFromOptions();
            if (style != null)
            {
                ApplyStyle(toolStrip, style);
            }
        }

        private static void ApplyStyle(MenuStrip menuStrip)
        {
            FlowBloxSyleBase style = GetStyleFromOptions();
            if (style != null)
            {
                ApplyStyle(menuStrip, style);
            }
        }

        private static void ApplyStyle(ToolStrip toolStrip, FlowBloxSyleBase colorTable)
        {
            toolStrip.Renderer = new ToolStripProfessionalRenderer(colorTable);
            toolStrip.BackColor = colorTable.ToolStripBackColor;
            foreach (ToolStripItem toolStripItem in toolStrip.Items)
            {
                toolStripItem.ForeColor = colorTable.MenuForeColor;
                if (toolStripItem is ToolStripDropDownButton)
                {
                    foreach (ToolStripItem DropDownItem in ((ToolStripDropDownButton)toolStripItem).DropDownItems)
                    {
                        DropDownItem.ForeColor = colorTable.MenuForeColor;
                    }
                }
            }
        }

        private static void ApplyStyle(MenuStrip menuStrip, FlowBloxSyleBase style)
        {
            menuStrip.Renderer = new ToolStripProfessionalRenderer(style);

            menuStrip.Font = style.MenuStripFont;
            menuStrip.BackColor = style.MenuBackColor;
            menuStrip.ForeColor = style.MenuForeColor;

            foreach (ToolStripMenuItem menuItem in menuStrip.Items)
            {
                ApplyStyle(menuItem, style);
            }
        }

        private static void ApplyStyle(ToolStripItem toolStripItem, FlowBloxSyleBase style)
        {
            toolStripItem.ForeColor = style.MenuForeColor;
            toolStripItem.ForeColor = style.MenuForeColor;

            if (toolStripItem is ToolStripDropDownItem)
            {
                foreach (ToolStripItem dropDownItem in ((ToolStripDropDownItem)toolStripItem).DropDownItems)
                {
                    ApplyStyle(dropDownItem, style);
                }
            }
        }

        private static void ApplyStyle(ContextMenuStrip contextMenu)
        {
            FlowBloxSyleBase style = GetStyleFromOptions();
            contextMenu.Renderer = new ToolStripProfessionalRenderer(style);
            contextMenu.ForeColor = style.MenuForeColor;
            contextMenu.Font = style.MenuStripFont;
            foreach (ToolStripMenuItem item in contextMenu.Items.OfType<ToolStripMenuItem>())
            {
                ApplyStyle(item, style);
            }
        }
    }
}