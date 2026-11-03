# FlowBlox theme

`FlowBloxTheme.xaml` is the single theme merged by `Views/MahAppMetroResources.xaml`.
It builds on MahApps `Light.Steel` and keeps the light form surfaces, blue display
labels, blue help badges and blue tooltips. Window-specific additions are:

- Dark title bars (`#343434`) with white text.
- A one-DIP smoke-white outer frame (`#E6E6E6`).
- A matching one-DIP separator between title bar and the light dialog header.
- Native Windows 11 rounded corners.

## Shared styles and resources

Dialogs merge `MahAppMetroResources.xaml` and reference
`Style="{DynamicResource FlowBlox.Window}"` on their `MetroWindow` root.
`FlowBlox.DialogHeader` styles the header border and `FlowBlox.DialogHeaderTitle`
styles its title. `PropertyViewStyles.xaml` provides the runtime label, text/code
editor, editor-container and help-icon styles. The resolver supplies bindings and
behavior; presentation is configured through the shared styles and resource keys.

All dialogs with a title/description area use these shared styles, including
extension management, project/version forms, test definitions and transactions.
Change `FlowBlox.Brush.DialogHeader` to recolor both parts together (currently
the original description light blue, `#EDF3F9`). Separate information and warning
panels retain their own semantic colors.

`FlowBlox.DialogDescription` shares the header background and adds a one-DIP gray
separator. `FlowBlox.DialogDescriptionText` uses normal-weight gray text. The
property window places the following section directly below this unified header
area; text spacing comes from the description's internal padding.

`FlowBlox.ToolTipStyle` in `FlowBloxCustomStyles.xaml` defines tooltip typography
and layout. Tooltip background, text, border thickness and corner radius come
from `FlowBloxTheme.xaml`. The original blue tooltip uses a two-DIP border and
eight-DIP radius. Resource changes also update an already open tooltip popup.

Embedded WPF views receive the same central resources through
`HostedControlStyles.xaml`.

## Native corners

`FlowBlox.Window.UseRoundedCorners` enables the regular native Windows 11 corner
radius through `WindowAppearanceBehavior`. MahApps retains its window chrome and
resize behavior. Windows controls corner clipping for maximized/snapped windows;
older Windows versions retain their supported window shape.
