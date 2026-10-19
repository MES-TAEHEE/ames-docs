namespace AMES.Web.Services;

// Circuit-only: a browser refresh creates a new state and hides the test menu again.
public sealed class WarehouseInboundTestState
{
    private int _clicks;
    public bool Visible {get;private set;}
    public void PickingClicked(bool allowed)
    { if(allowed && ++_clicks>=3)Visible=true; }
}
