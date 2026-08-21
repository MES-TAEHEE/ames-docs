namespace AMES.Tablet;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage())
        {
            Title = "A-MES Forklift Inventory",
            Width = 1280,
            Height = 800,
            MinimumWidth = 960,
            MinimumHeight = 600
        };
	}
}
