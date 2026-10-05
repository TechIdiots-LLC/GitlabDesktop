using GitLabDesktop.Services;

namespace GitLabDesktop.Views;

/// <summary>A heading and the picker items under it.</summary>
public sealed class PickerGroup(string name, IEnumerable<PickerItem> items) : List<PickerItem>(items)
{
    public string Name { get; } = name;
}

/// <summary>
/// A searchable list that drops down under a toolbar button, like GitHub Desktop's repository and branch lists,
/// instead of covering the window. Items with a <see cref="PickerItem.Group"/> are shown under headings, and
/// actions (such as "New branch") are buttons beside the filter. A click outside the panel or Esc cancels.
/// </summary>
public partial class PickerDropdown : ContentView
{
    readonly List<PickerItem> _items;
    readonly bool _grouped;
    // Continuations must not run inside the list's selection/pointer handlers (see Finish).
    readonly TaskCompletionSource<PickerItem?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _finishing;

    public Task<PickerItem?> Result => _result.Task;

    /// <summary>Raised once the dropdown has a result and should be removed from the page.</summary>
    public event EventHandler? Closed;

    public PickerDropdown(IReadOnlyList<PickerItem> items, IReadOnlyList<PickerItem> actions, string? emptyText)
    {
        InitializeComponent();
        _items = items.ToList();
        _grouped = _items.Any(i => i.Group is not null);
        List.IsGrouped = _grouped;
        if (emptyText is not null) EmptyLabel.Text = emptyText;
        Show(_items);

        foreach (var action in actions)
        {
            var button = new Button { Text = action.Title, Style = (Style)Application.Current!.Resources["SecondaryButton"], VerticalOptions = LayoutOptions.Center };
            button.Clicked += (_, _) => Finish(action);
            Header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Header.Add(button, Header.ColumnDefinitions.Count - 1, 0);
        }

        Loaded += (_, _) => Search.Focus();
        HandlerChanged += (_, _) => HookKeys();
    }

    /// <summary>Places the panel under a toolbar button: from its left edge, below it, down to the window's bottom.</summary>
    public void Place(double x, double top, double width)
    {
        Panel.Margin = new Thickness(x, top, 0, 0);
        Panel.WidthRequest = width;
    }

    void Show(IEnumerable<PickerItem> items)
    {
        List.ItemsSource = _grouped
            ? items.GroupBy(i => i.Group ?? "").Select(g => new PickerGroup(g.Key, g)).ToList()
            : items.ToList();
    }

    IEnumerable<PickerItem> Filtered()
    {
        var q = Search.Text?.Trim();
        return string.IsNullOrEmpty(q)
            ? _items
            : _items.Where(i => i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                (i.Subtitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    void OnSearchChanged(object? sender, TextChangedEventArgs e) => Show(Filtered());

    // Enter picks the first match, so typing part of a name and pressing Enter switches to it.
    void OnSearchSubmitted(object? sender, EventArgs e)
    {
        if (Filtered().FirstOrDefault() is { } first) Finish(first);
    }

    void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is PickerItem item) Finish(item);
    }

    void OnOutsideTapped(object? sender, TappedEventArgs e) => Finish(null);

    public void Cancel() => Finish(null);

    /// <summary>
    /// Closes after the current input event has finished, then hands back the result. Removing the list (and letting
    /// the caller rebuild the main window) from inside its click handling tears down UI that WinUI is still
    /// dispatching to, which crashes in Microsoft.UI.Xaml.dll (0xc000027b).
    /// </summary>
    void Finish(PickerItem? item)
    {
        if (_finishing) return;
        _finishing = true;
        Dispatcher.Dispatch(() =>
        {
            try
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _result.TrySetResult(item);
            }
        });
    }

    void HookKeys()
    {
#if WINDOWS
        // The search box swallows Esc, so listen for handled key presses too.
        if (Handler?.PlatformView is Microsoft.UI.Xaml.UIElement view)
        {
            view.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent,
                new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Escape)
                    {
                        e.Handled = true;
                        Cancel();
                    }
                }), handledEventsToo: true);
        }
#endif
    }
}
