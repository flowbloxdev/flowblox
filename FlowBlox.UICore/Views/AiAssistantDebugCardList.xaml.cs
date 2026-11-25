using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FlowBlox.UICore.ViewModels;

namespace FlowBlox.UICore.Views;

/// <summary>
/// Keeps native extended ListBox selection aligned with the shared editor selection after live updates.
/// </summary>
public partial class AiAssistantDebugCardList : UserControl
{
    public static readonly DependencyProperty CardTemplateProperty = DependencyProperty.Register(
        nameof(CardTemplate), typeof(DataTemplate), typeof(AiAssistantDebugCardList));

    private AiAssistantDebugViewModel? _viewModel;
    private bool _synchronizing;

    public DataTemplate CardTemplate
    {
        get => (DataTemplate)GetValue(CardTemplateProperty);
        set => SetValue(CardTemplateProperty, value);
    }

    public AiAssistantDebugCardList() => InitializeComponent();

    private void Control_Loaded(object sender, RoutedEventArgs args) => Attach(DataContext as AiAssistantDebugViewModel);
    private void Control_Unloaded(object sender, RoutedEventArgs args) => Attach(null);

    private void Control_DataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (IsLoaded)
            Attach(args.NewValue as AiAssistantDebugViewModel);
    }

    private void Attach(AiAssistantDebugViewModel? viewModel)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= ViewModelChanged;
        _viewModel = viewModel;
        if (_viewModel != null)
            _viewModel.PropertyChanged += ViewModelChanged;
        SynchronizeSelection();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AiAssistantDebugViewModel.SelectedCards))
            SynchronizeSelection();
    }

    private void SynchronizeSelection()
    {
        if (_viewModel == null || CardList == null)
            return;

        if (CardList.SelectedItems.Count == _viewModel.SelectedCards.Count &&
            _viewModel.SelectedCards.All(card => CardList.SelectedItems.Contains(card)))
            return;

        _synchronizing = true;
        try
        {
            CardList.SelectedItems.Clear();
            foreach (var card in _viewModel.SelectedCards)
                CardList.SelectedItems.Add(card);
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private void Cards_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_synchronizing && IsVisible)
            _viewModel?.SetSelectedCards(CardList.SelectedItems.Cast<AiAssistantDebugCard>().ToArray());
    }
}
