using Edvaniq.ServiceDefaults;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    // Kestrel listens on it next to the app port (ASPNETCORE_HTTP_PORTS in deploy/compose.yml). It is never published,
    // so only the container itself and the app's own network reach it.
    private const int InternalHealthPort = 8081;

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddTraceIds();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.AddHostedService<StudyDayCheck>();

        builder.Services.AddHostedService<MemoryLimitLogger>();

        builder.Services.AddHostedService<CommitLogger>();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                // Edvaniq.Messaging: MessageTrace.SourceName, the consumers' activities
                tracing.AddSource(builder.Environment.ApplicationName, "Edvaniq.Messaging")
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    // One id follows a request through web host, gateway and service: the W3C trace id, which OpenTelemetry passes on in
    // traceparent. Every problem answer names it as traceId, the bare 32 hex digits and nothing else about the inside.
    // Outside Development the console is the log, one line per entry with the scopes, so each line carries it as TraceId.
    // An error report thus leads to the log lines of every process the request went through.
    public static TBuilder AddTraceIds<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.GetTraceId());

        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.IncludeScopes = true;
            });
        }

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        var alivenessOptions = new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        };

        // Health answers Healthy, Degraded or Unhealthy, never an exception. Only a check tagged "details" that isn't
        // healthy adds a line with its name and description, which it keeps free of user data (the messaging check of
        // BuildingBlocks). Docker keeps the answer, so the server's alarm can name what is wrong. Alive keeps the default.
        // Health never needs a token, Docker asks without one.
        var healthOptions = new HealthCheckOptions { ResponseWriter = WriteHealthAsync };
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath, healthOptions).AllowAnonymous();

            app.MapHealthChecks(AlivenessEndpointPath, alivenessOptions).AllowAnonymous();
        }
        else
        {
            // Outside Development only on the internal port. UseHealthChecks matches the port the connection came in on,
            // while RequireHost("*:8081") would trust the Host header, which a client can fake. On the app port both
            // paths answer 404. As middleware they are no endpoints, so the service must call UseAuthentication and
            // UseAuthorization after MapDefaultEndpoints, or the fallback policy rejects them too.
            app.UseHealthChecks(HealthEndpointPath, InternalHealthPort, healthOptions);
            app.UseHealthChecks(AlivenessEndpointPath, InternalHealthPort, alivenessOptions);
        }

        return app;
    }

    // The tag of a check whose description /health may show.
    public const string HealthDetailsTag = "details";

    internal static Task WriteHealthAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        var details = report.Entries
            .Where(entry => entry.Value.Status != HealthStatus.Healthy
                && entry.Value.Tags.Contains(HealthDetailsTag)
                && !string.IsNullOrEmpty(entry.Value.Description))
            .Select(entry => $"{entry.Key}: {entry.Value.Description!.ReplaceLineEndings(" ")}");
        return context.Response.WriteAsync(string.Join('\n', [report.Status.ToString(), .. details]));
    }
}