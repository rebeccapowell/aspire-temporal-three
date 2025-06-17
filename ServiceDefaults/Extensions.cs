using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
	public static TBuilder AddServiceDefaults<TBuilder>(
		this TBuilder builder,
		Action<MeterProviderBuilder>? metricsConfig = null,
		Action<TracerProviderBuilder>? tracingConfig = null)
		where TBuilder : IHostApplicationBuilder
	{
		builder.ConfigureOpenTelemetry(metricsConfig, tracingConfig);
		builder.AddDefaultHealthChecks();

		builder.Services.AddServiceDiscovery();

		builder.Services.ConfigureHttpClientDefaults(http =>
		{
			http.AddStandardResilienceHandler();
			http.AddServiceDiscovery();
		});

		return builder;
	}

	public static TBuilder ConfigureOpenTelemetry<TBuilder>(
		this TBuilder builder,
		Action<MeterProviderBuilder>? metricsConfig = null,
		Action<TracerProviderBuilder>? tracingConfig = null)
		where TBuilder : IHostApplicationBuilder
	{
		builder.Logging.AddOpenTelemetry(logging =>
		{
			logging.IncludeFormattedMessage = true;
			logging.IncludeScopes = true;
			logging.ParseStateValues = true;
		});

		builder.Services.AddOpenTelemetry()
			.WithMetrics(metrics =>
			{
				metrics.AddAspNetCoreInstrumentation()
					.AddHttpClientInstrumentation()
					.AddRuntimeInstrumentation();

				metricsConfig?.Invoke(metrics);
			})
			.WithTracing(tracing =>
			{
				tracing.AddSource(builder.Environment.ApplicationName)
					.AddAspNetCoreInstrumentation()
					.AddHttpClientInstrumentation();

				tracingConfig?.Invoke(tracing);
			});

		builder.AddOpenTelemetryExporters();

		return builder;
	}

	private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

		if (useOtlpExporter)
		{
			builder.Services.ConfigureOpenTelemetryMeterProvider(mb => mb.AddOtlpExporter());
			builder.Services.ConfigureOpenTelemetryTracerProvider(tb => tb.AddOtlpExporter());
			builder.Logging.AddOpenTelemetry(logs => logs.AddOtlpExporter());
		}

		return builder;
	}

	public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		builder.Services.AddHealthChecks()
			.AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

		return builder;
	}

	public static WebApplication MapDefaultEndpoints(this WebApplication app)
	{
		if (app.Environment.IsDevelopment())
		{
			app.MapHealthChecks("/health");
			app.MapHealthChecks("/alive", new HealthCheckOptions
			{
				Predicate = r => r.Tags.Contains("live")
			});
		}

		return app;
	}
}