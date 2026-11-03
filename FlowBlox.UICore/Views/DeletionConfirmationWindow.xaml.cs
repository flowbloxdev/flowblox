using FlowBlox.Enums;
using FlowBlox.Core;
using FlowBlox.Core.Models.Deletion;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FlowBlox.UICore.Views
{
    public partial class DeletionConfirmationWindow : MetroWindow
    {
        public DeletionConfirmationWindow(DeletionReport report)
        {
            InitializeComponent();
            DataContext = new ConfirmationViewModel(report);
        }

        private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void RootCards_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var list = (ListBox)sender;
            var item = ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null && item.IsSelected && Keyboard.Modifiers == ModifierKeys.None)
            {
                list.Focus();
                list.SelectedItem = null;
                e.Handled = true;
            }
        }

        private void RootCards_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var list = (ListBox)sender;
            if (e.Key == Key.Escape && list.SelectedItem != null)
            {
                list.SelectedItem = null;
                e.Handled = true;
            }
        }

        private sealed class ConfirmationViewModel : INotifyPropertyChanged
        {
            public ConfirmationViewModel(DeletionReport report)
            {
                RootTargets = report.Dependencies
                    .GroupBy(x => x.EditTarget ?? x.ReferencingObject, ReferenceEqualityComparer.Instance)
                    .Select(CreateRootCard)
                    .ToList();
                selectedRoot = RootTargets.FirstOrDefault();
            }

            public string Title => L("DeletionConfirmation_Title");
            public string HeaderTitle => L("DeletionConfirmation_Header");
            public string Description => L("DeletionConfirmation_Description");
            public string BackText => L("DeletionConfirmation_Back");
            public string DeleteText => L("DeletionConfirmation_Delete");
            public string RootTargetsHeader => V("RootTargetsHeader");
            public string MessagesHeader => V("MessagesHeader");
            public string SelectionHint => V("SelectionHint");
            public IReadOnlyList<RootCard> RootTargets { get; }
            public IReadOnlyList<ConfirmationEntry> Entries => SelectedRoot?.Entries ?? Array.Empty<ConfirmationEntry>();
            public bool HasEntries => Entries.Count > 0;
            public string EmptyStateText => V(SelectedRoot == null ? "NoSelection" : "NoMessages");

            private RootCard? selectedRoot;
            public RootCard? SelectedRoot
            {
                get => selectedRoot;
                set
                {
                    if (ReferenceEquals(selectedRoot, value))
                        return;

                    selectedRoot = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRoot)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Entries)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEntries)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EmptyStateText)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private static RootCard CreateRootCard(IGrouping<object, DeletionDependency> group)
            {
                var dependencies = group.OrderByDescending(x => x.ReviewStatus).ToList();
                var status = dependencies[0].ReviewStatus;
                var summary = dependencies
                    .GroupBy(x => x.ReviewStatus)
                    .Select(x => $"{L("DeletionConfirmation_Status_" + x.Key)} ({x.Count()})")
                    .ToList();

                return new RootCard
                {
                    ObjectType = FlowBloxComponentHelper.GetDisplayName(group.Key),
                    ObjectName = group.Key.ToString() ?? string.Empty,
                    Summary = summary,
                    Entries = dependencies.Select(CreateEntry).ToList(),
                    Palette = GetPalette(status),
                    Icon = GetIcon(status)
                };
            }

            private static PackIconMaterialKind GetIcon(DeletionReviewStatus status) => status switch
            {
                DeletionReviewStatus.Information => PackIconMaterialKind.InformationOutline,
                DeletionReviewStatus.ReviewRecommended => PackIconMaterialKind.ClipboardTextSearchOutline,
                _ => PackIconMaterialKind.AlertOutline
            };

            private static ConfirmationEntry CreateEntry(DeletionDependency dependency)
            {
                var status = dependency.ReviewStatus;
                var palette = GetPalette(status);
                var objectType = FlowBloxComponentHelper.GetDisplayName(dependency.ReferencingObject);
                var objectName = dependency.ReferencingObject.ToString();
                if (dependency.EditTarget != null &&
                    !ReferenceEquals(dependency.EditTarget, dependency.ReferencingObject))
                {
                    objectType = $"{FlowBloxComponentHelper.GetDisplayName(dependency.EditTarget)} \u2192 {objectType}";
                    objectName = $"{dependency.EditTarget} \u2192 {objectName}";
                }

                return new ConfirmationEntry
                {
                    ObjectType = objectType,
                    ObjectName = objectName ?? string.Empty,
                    PropertyLabel = L("DeletionConfirmation_Property"),
                    Property = dependency.Property.GetCustomAttributes(typeof(DisplayAttribute), true)
                        .OfType<DisplayAttribute>().FirstOrDefault()?.GetName() ?? dependency.Property.Name,
                    ImpactLabel = L("DeletionConfirmation_Impact"),
                    Impact = $"{L("DeletionConfirmation_Impact_" + dependency.Impact)}: " +
                             dependency.ReferencedObject,
                    Status = L("DeletionConfirmation_Status_" + status),
                    Icon = GetIcon(status),
                    Palette = palette
                };
            }

            private static CardPalette GetPalette(DeletionReviewStatus status) => status switch
            {
                DeletionReviewStatus.Information => new CardPalette(
                    B(244, 248, 252), B(184, 211, 234), B(221, 235, 247), B(230, 240, 248), B(58, 110, 165)),
                DeletionReviewStatus.ReviewRecommended => new CardPalette(
                    B(255, 251, 235), B(242, 211, 139), B(254, 243, 199), B(254, 240, 180), B(146, 94, 9)),
                _ => new CardPalette(
                    B(255, 244, 242), B(228, 184, 178), B(247, 219, 215), B(243, 216, 212), B(153, 66, 58))
            };

            private static Brush B(byte red, byte green, byte blue)
            {
                var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
                brush.Freeze();
                return brush;
            }
        }

        private abstract class ConfirmationCard
        {
            public required string ObjectType { get; init; }
            public required string ObjectName { get; init; }
            public PackIconMaterialKind Icon { get; init; }
            public required CardPalette Palette { get; init; }
            public Brush CardBackground => Palette.CardBackground;
            public Brush CardBorder => Palette.CardBorder;
            public Brush IconBackground => Palette.IconBackground;
            public Brush StatusBackground => Palette.StatusBackground;
            public Brush Accent => Palette.Accent;
        }

        private sealed class RootCard : ConfirmationCard
        {
            public required IReadOnlyList<string> Summary { get; init; }
            public required IReadOnlyList<ConfirmationEntry> Entries { get; init; }
        }

        private sealed class ConfirmationEntry : ConfirmationCard
        {
            public string PropertyLabel { get; init; }
            public string Property { get; init; }
            public string ImpactLabel { get; init; }
            public string Impact { get; init; }
            public string Status { get; init; }
        }

        private sealed record CardPalette(
            Brush CardBackground,
            Brush CardBorder,
            Brush IconBackground,
            Brush StatusBackground,
            Brush Accent);

        private static string V(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(Resources.DeletionConfirmationWindow));

        private static string L(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(FlowBloxTexts));
    }
}
