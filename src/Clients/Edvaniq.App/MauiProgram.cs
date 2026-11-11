using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Shell;
using Microsoft.AspNetCore.Components.Authorization;
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

		// Login, ping, the learning material and the quiz share one client, so they carry the session cookie of the login. The host answers within
		// 9 seconds, its wait for a token or for Identity included, with an error of its own if need be.
		var session = new HttpClient
		{
			BaseAddress = new Uri("https://edvaniq.nullradix.de/"),
			Timeout = TimeSpan.FromSeconds(20),
		};
		builder.Services.AddSingleton<IPing>(new HttpPing(session));
		builder.Services.AddSingleton<IWebSession>(new HttpWebSession(session));
		builder.Services.AddSingleton<ILearningMaterial>(new HttpLearningMaterial(session));
		builder.Services.AddSingleton<IQuiz>(new HttpQuiz(session));

		builder.Services.AddSingleton<WindowReturnSignal>();
		builder.Services.AddSingleton<IReturnSignal>(services => services.GetRequiredService<WindowReturnSignal>());

		builder.Services.AddAuthorizationCore();
		builder.Services.AddCascadingAuthenticationState();
		builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationState>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}