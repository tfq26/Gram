namespace Gram.Maui;

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
			Title = "Gram",
			Width = 1240,
			Height = 800,
			MinimumWidth = 920,
			MinimumHeight = 560
		};
	}
}
