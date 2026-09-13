namespace Gram.Maui;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
		blazorWebView.HorizontalOptions = LayoutOptions.Fill;
		blazorWebView.VerticalOptions = LayoutOptions.Fill;

		blazorWebView.BlazorWebViewInitialized += (sender, args) =>
		{
#if WINDOWS
			if (args.WebView is Microsoft.UI.Xaml.Controls.WebView2 wv2)
			{
				wv2.CoreWebView2Initialized += (s, e) =>
				{
					try
					{
						wv2.CoreWebView2.Settings.AreDevToolsEnabled = true;
					}
					catch {}
				};
			}
#endif
		};
	}
}
