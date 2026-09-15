namespace AMES.Web.Services;

public sealed class ScreenCatalogNotifier
{
    public event Action? Changed;

    public void Notify() => Changed?.Invoke();
}
