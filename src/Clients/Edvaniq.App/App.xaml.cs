namespace Edvaniq.App;

public partial class App : Application
{
	private readonly WindowReturnSignal returnSignal;

	public App(WindowReturnSignal returnSignal)
	{
		this.returnSignal = returnSignal;
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "Edvaniq.App" };
		returnSignal.Watch(window);
		return window;
	}
}
