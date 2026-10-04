using GitLabDesktop.Views;

namespace GitLabDesktop.Services;

public sealed record PickerItem(string Title, string? Subtitle, object Value, string? Badge = null);

/// <summary>Alerts, prompts and modal pages shown on top of whatever is currently displayed.</summary>
public sealed class DialogService
{
    static Page Top
    {
        get
        {
            var root = Application.Current!.Windows[0].Page!;
            return root.Navigation.ModalStack.LastOrDefault() ?? root;
        }
    }

    public Task AlertAsync(string title, string message)
        => MainThread.InvokeOnMainThreadAsync(() => Top.DisplayAlertAsync(title, message, "OK"));

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
        => MainThread.InvokeOnMainThreadAsync(() => Top.DisplayAlertAsync(title, message, accept, cancel));

    public Task<string?> PromptAsync(string title, string message, string? initial = null, string accept = "OK", string? placeholder = null)
        => MainThread.InvokeOnMainThreadAsync(() =>
            Top.DisplayPromptAsync(title, message, accept, "Cancel", placeholder, initialValue: initial ?? ""));

    /// <returns>The chosen button text, or null when cancelled.</returns>
    public async Task<string?> ChooseAsync(string title, params string[] buttons)
    {
        var r = await MainThread.InvokeOnMainThreadAsync(() => Top.DisplayActionSheetAsync(title, "Cancel", null, buttons));
        return r is null or "Cancel" ? null : r;
    }

    /// <summary>Searchable list; returns the chosen item's value, or null when cancelled.</summary>
    public async Task<T?> PickAsync<T>(string title, IEnumerable<PickerItem> items, string? emptyText = null) where T : class
    {
        var page = new PickerPage(title, items.ToList(), emptyText);
        await Top.Navigation.PushModalAsync(page);
        var result = await page.Result;
        return result?.Value as T;
    }

    public Task PushModalAsync(Page page) => Top.Navigation.PushModalAsync(page);

    public Task PopModalAsync() => Top.Navigation.PopModalAsync();
}
