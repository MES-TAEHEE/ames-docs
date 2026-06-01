namespace AMES.Pop.Services;

public enum ToastKind { Ok, Warn, Bad, Info }

public sealed record Toast(string Text, ToastKind Kind, Guid Id, DateTime ShownAt);

/// <summary>
/// App-wide toast bus. Pages call Ok/Warn/Bad/Info from any handler;
/// ToastHost subscribes and renders + auto-dismisses after 4 s.
/// Singleton — registered in PopBlazorForm.
/// </summary>
public sealed class ToastService
{
    public event Action<Toast>? OnShow;

    public void Show(string text, ToastKind kind = ToastKind.Info)
        => OnShow?.Invoke(new Toast(text, kind, Guid.NewGuid(), DateTime.Now));

    public void Ok(string text)   => Show(text, ToastKind.Ok);
    public void Warn(string text) => Show(text, ToastKind.Warn);
    public void Bad(string text)  => Show(text, ToastKind.Bad);
    public void Info(string text) => Show(text, ToastKind.Info);
}
