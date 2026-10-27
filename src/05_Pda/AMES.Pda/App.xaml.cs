namespace AMES.Pda;

public partial class App : Application
{
	private readonly Services.AuthState _auth;
	private readonly Services.SessionValidator _sessions;

	public App(Services.AuthState auth, Services.SessionValidator sessions)
	{
		_auth = auth;
		_sessions = sessions;
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "AMES.Pda" };
		window.Stopped += (_, _) => _auth.RequireSessionCheck();
		window.Resumed += async (_, _) => await _sessions.CheckAsync();
		window.Activated += async (_, _) => await _sessions.CheckOnActivationAsync();
		return window;
	}
}
