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

		// Login, ping, the learning material and the quiz share one handler, so they carry the session cookie of the login. The host answers within
		// 9 seconds, its wait for a token or for Identity included, with an error of its own if need be. Every call under /api goes past the watch.
		var host = new Uri("https://edvaniq.nullradix.de/");
		var cookies = new HttpClientHandler();
		HttpClient Watched(IServiceProvider services) =>
			new(new SessionWatchHandler(services.GetRequiredService<SessionWatch>()) { InnerHandler = cookies }, disposeHandler: false)
			{
				BaseAddress = host,
				Timeout = TimeSpan.FromSeconds(20),
			};

		// What a run-out login touches: the watch that notices it and the drafts that keep what was not sent.
		builder.Services.AddScoped<SessionWatch>();
		builder.Services.AddScoped<Drafts>();
		builder.Services.AddScoped<IPing>(services => new HttpPing(Watched(services)));
		builder.Services.AddSingleton<IWebSession>(new HttpWebSession(new HttpClient(cookies, disposeHandler: false)
		{
			BaseAddress = host,
			Timeout = TimeSpan.FromSeconds(20),
		}));
		builder.Services.AddScoped<ILearningMaterial>(services => new HttpLearningMaterial(Watched(services)));
		builder.Services.AddScoped<IQuiz>(services => new HttpQuiz(Watched(services)));
		builder.Services.AddScoped<ITodayPlan>(services => new HttpTodayPlan(Watched(services)));
		builder.Services.AddScoped<ILearningGoals>(services => new HttpLearningGoals(Watched(services)));
		builder.Services.AddScoped<ILearningProgress>(services => new HttpLearningProgress(Watched(services)));

		builder.Services.AddSingleton<WindowReturnSignal>();
		builder.Services.AddSingleton<IReturnSignal>(services => services.GetRequiredService<WindowReturnSignal>());

		builder.Services.AddAuthorizationCore();
		builder.Services.AddCascadingAuthenticationState();
		builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationState>();
		builder.Services.AddScoped(services => new AnswerSync(services.GetRequiredService<ILearningProgress>(), services.GetRequiredService<AuthenticationStateProvider>()));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
