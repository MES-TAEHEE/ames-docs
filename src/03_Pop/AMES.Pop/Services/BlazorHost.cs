namespace AMES.Pop.Services;

/// <summary>
/// One-way channel for Blazor components to signal the WinForms host.
/// Components call <see cref="RaiseAction"/> when the operator taps a nav /
/// logout / picker button; the WinForms <c>PopBlazorForm</c> subscribes
/// once on construction and dispatches accordingly. Keeps Razor pages free
/// of any direct WinForms or Form references.
/// </summary>
public static class BlazorHost
{
    /// <summary>Fired by .razor pages on nav / logout / etc.</summary>
    public static event Func<string, Task>? ActionRequested;

    public static Task RaiseAction(string action)
        => ActionRequested?.Invoke(action) ?? Task.CompletedTask;
}
