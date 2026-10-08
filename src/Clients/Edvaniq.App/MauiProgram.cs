using Edvaniq.Client.Core;
using Microsoft.Extensions.Logging;

namespace Edvaniq.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		// Until the app gets its own configuration it asks the public web host, like the browser does.
		builder.Services.AddSingleton<IBackendStatus>(new HttpBackendStatus(new HttpClient
		{
			BaseAddress = new Uri("https://edvaniq.nullradix.de/"),
			Timeout = TimeSpan.FromSeconds(5),
		}));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
