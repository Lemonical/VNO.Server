using System.Collections.Specialized;
using Avalonia.Controls;
using VNO.Server.ViewModels;

namespace VNO.Server.Views;

/// <summary>
/// The chat page, OOC with a server voice, read only IC, and the event log
/// </summary>
/// <remarks>
/// The only code behind is keeping the feeds pinned to the newest line, a purely
/// visual concern the view model should not know about
/// </remarks>
public sealed partial class ChatPageView : UserControl
{
    private ChatViewModel? _viewModel;

    /// <summary>
    /// Creates the view
    /// </summary>
    public ChatPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.OocMessages.CollectionChanged -= OnFeedChanged;
                _viewModel.IcMessages.CollectionChanged -= OnFeedChanged;
                _viewModel.Events.CollectionChanged -= OnFeedChanged;
            }
            _viewModel = DataContext as ChatViewModel;
            if (_viewModel is not null)
            {
                _viewModel.OocMessages.CollectionChanged += OnFeedChanged;
                _viewModel.IcMessages.CollectionChanged += OnFeedChanged;
                _viewModel.Events.CollectionChanged += OnFeedChanged;
            }
        };
    }

    private void OnFeedChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OocScroll.ScrollToEnd();
        IcScroll.ScrollToEnd();
        EventsScroll.ScrollToEnd();
    }
}
