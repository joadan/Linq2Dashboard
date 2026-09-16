using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// The input for a text facet (concept §5, design §9). The typed text is component-local until it is
/// applied as a <see cref="TextSelection"/>, after a pause or on Enter, because every new text costs
/// a scan over the dataset. The applied text lives in the selections like any other selection.
/// </summary>
public partial class TextFacet<T>
{
    private string text = string.Empty;
    private CancellationTokenSource? pending;
    private bool collapsed;
    private bool? lastCollapsedParameter;

    /// <summary>The facet key, as defined in the builder. Must be a text facet.</summary>
    [Parameter, EditorRequired]
    public string Key { get; set; } = default!;

    /// <summary>Overrides the name given in the builder, which is only a default display name, for example with a localised string (concept §7).</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Replaces the default header (name and clear button). Receives the facet state. Collapsing is then controlled only through <see cref="Collapsed"/>.</summary>
    [Parameter]
    public RenderFragment<TextFacetState>? HeaderTemplate { get; set; }

    /// <summary>Let the user collapse the facet from its header. On by default; set false for a fixed header.</summary>
    [Parameter]
    public bool Collapsible { get; set; } = true;

    /// <summary>Whether the body is hidden. Bindable: <c>@@bind-Collapsed</c> follows the user's toggling, and setting it applies from the host.</summary>
    [Parameter]
    public bool Collapsed { get; set; }

    /// <summary>Raised when the user toggles the header; the second half of <c>@bind-Collapsed</c>.</summary>
    [Parameter]
    public EventCallback<bool> CollapsedChanged { get; set; }

    /// <summary>
    /// How long the user must pause before the text is applied. Enter applies at once. Default 300 ms;
    /// zero applies on every keystroke, which is fine for small datasets.
    /// </summary>
    [Parameter]
    public int DebounceMilliseconds { get; set; } = 300;

    /// <summary>Placeholder of the input.</summary>
    [Parameter]
    public string Placeholder { get; set; } = "Type to filter";

    /// <summary>
    /// Classes for the text input, replacing the library's default look (<c>l2d-input</c>) so a CSS framework's
    /// class takes over cleanly, for example <c>form-control</c>. The hook class <c>l2d-text-input</c> stays (design §9).
    /// </summary>
    [Parameter]
    public string? InputClass { get; set; }

    /// <summary>Accessible label and tooltip of the header's clear button, which shows an ×.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    private string HeaderName => Name ?? Facet.Name;

    private TextFacetState Facet => State.Facet(Key) as TextFacetState
        ?? throw new InvalidOperationException($"Facet '{Key}' is not a text facet; use the component for its kind.");

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();
        text = Facet.Text ?? string.Empty;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Collapsed != lastCollapsedParameter)
        {
            lastCollapsedParameter = Collapsed;
            collapsed = Collapsed;
        }
    }

    /// <summary>Follows the applied text when it changed elsewhere (a chip removed, the host set new selections), unless the user is mid-typing.</summary>
    protected override void OnDashboardStateChanged()
    {
        string applied = Facet.Text ?? string.Empty;
        if (pending is null && new TextSelection(text).Text != applied)
        {
            text = applied;
        }
    }

    /// <summary>Cancels a pending debounce.</summary>
    protected override void Disposing() => CancelPending();

    private Task OnInput(ChangeEventArgs e)
    {
        text = e.Value?.ToString() ?? string.Empty;
        if (DebounceMilliseconds <= 0)
        {
            return ApplyAsync();
        }

        CancelPending();
        var cts = new CancellationTokenSource();
        pending = cts;
        _ = ApplyAfterPauseAsync(cts);
        return Task.CompletedTask;
    }

    private Task OnKeyDown(KeyboardEventArgs e) => e.Key == "Enter" ? ApplyAsync() : Task.CompletedTask;

    private async Task ApplyAfterPauseAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(cts, pending))
        {
            return;
        }

        await InvokeAsync(ApplyAsync);
    }

    /// <summary>Applies the typed text. A blank text clears the facet through <see cref="Selections.With"/>.</summary>
    private Task ApplyAsync()
    {
        CancelPending();
        return Context.SelectAsync(Key, new TextSelection(text));
    }

    private Task ClearAsync()
    {
        CancelPending();
        text = string.Empty;
        return Context.ClearAsync(Key);
    }

    private void CancelPending()
    {
        if (pending is CancellationTokenSource cts)
        {
            pending = null;
            cts.Cancel();
            cts.Dispose();
        }
    }

    private Task ToggleCollapsed()
    {
        collapsed = !collapsed;
        lastCollapsedParameter = collapsed;
        return CollapsedChanged.InvokeAsync(collapsed);
    }
}
