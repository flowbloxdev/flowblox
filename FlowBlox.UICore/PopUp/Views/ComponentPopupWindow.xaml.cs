using FlowBlox.UICore.Commands;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FlowBlox.Core.Logging;

namespace FlowBlox.UICore.PopUp.Views
{
    public partial class ComponentPopupWindow : MetroWindow
    {
        private static readonly SolidColorBrush ActiveStepBrush = new(Color.FromRgb(30, 126, 223));
        private static readonly SolidColorBrush InactiveStepBrush = new(Color.FromRgb(207, 216, 226));
        private readonly IReadOnlyList<ComponentPopupItem> _items;
        private int _currentIndex;

        public ComponentPopupWindow(string title, IReadOnlyList<ComponentPopupItem> items)
        {
            InitializeComponent();

            DescriptionMarkdown.Engine.HyperlinkCommand = new RelayCommand(parameter =>
                OpenLink(parameter?.ToString() ?? string.Empty));

            Title = title;
            _items = items ?? throw new ArgumentNullException(nameof(items));
            ShowCurrentItem();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex >= _items.Count - 1)
            {
                DialogResult = true;
                Close();
                return;
            }

            _currentIndex++;
            ShowCurrentItem();
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex == 0)
                return;

            _currentIndex--;
            ShowCurrentItem();
        }

        private void ShowCurrentItem()
        {
            var item = _items[_currentIndex];
            var isLastItem = _currentIndex >= _items.Count - 1;

            HeadlineTextBlock.Text = item.Headline;
            SetDescription(item.Description);
            StepImage.Source = item.Image;
            StepImage.Visibility = item.Image != null ? Visibility.Visible : Visibility.Collapsed;
            ImagePlaceholderTextBlock.Visibility = item.Image == null ? Visibility.Visible : Visibility.Collapsed;
            PreviousButton.Visibility = _currentIndex > 0 ? Visibility.Visible : Visibility.Collapsed;

            NextButtonTextBlock.Text = isLastItem
                ? ComponentPopupWindowResources.Button_Close
                : ComponentPopupWindowResources.Button_Next;
            NextButtonIcon.Kind = isLastItem
                ? PackIconMaterialKind.Check
                : PackIconMaterialKind.ArrowRight;

            StepIndicator.ItemsSource = Enumerable
                .Range(0, _items.Count)
                .Select(i => i == _currentIndex ? ActiveStepBrush : InactiveStepBrush)
                .ToList();
        }

        private void SetDescription(string description)
        {
            DescriptionMarkdown.Markdown = description ?? string.Empty;
        }

        private static void OpenLink(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps &&
                 uri.Scheme != Uri.UriSchemeFile))
                return;

            var target = uri.IsFile ? uri.LocalPath : uri.AbsoluteUri;
            try
            {
                if (uri.IsFile && !File.Exists(target))
                    return;

                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    $"Failed to open component popup link '{target}'.",
                    ex);
            }
        }
    }
}
