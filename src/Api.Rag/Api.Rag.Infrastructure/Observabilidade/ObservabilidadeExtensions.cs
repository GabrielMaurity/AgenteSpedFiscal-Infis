using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Api.Rag.Infrastructure.Observabilidade;

public static class ObservabilidadeExtensions
{
    /// <summary>
    /// Traces, métricas e logs por OpenTelemetry. Os demais provedores de log são removidos para
    /// que todo log passe pelo mascaramento (P-18). O OTLP é ativado por OTEL_EXPORTER_OTLP_ENDPOINT.
    /// </summary>
    public static IHostApplicationBuilder AddObservabilidade(this IHostApplicationBuilder builder, string nomeServico)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddOpenTelemetry(opcoes =>
        {
            opcoes.IncludeFormattedMessage = true;
            opcoes.IncludeScopes = true;
            opcoes.AddProcessor(new MascaramentoLogProcessor());
        });

        var telemetria = builder.Services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(nomeServico))
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
            .WithMetrics(metricas => metricas.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetria.UseOtlpExporter();
        }

        return builder;
    }
}

/// <summary>Remove CNPJ e CPF de textos destinados a logs.</summary>
public static partial class MascaradorDadosSensiveis
{
    public const string Mascara = "***";

    public static string Mascarar(string texto)
    {
        var resultado = CnpjFormatado().Replace(texto, Mascara);
        resultado = CnpjSemFormatacao().Replace(resultado, Mascara);
        return Cpf().Replace(resultado, Mascara);
    }

    [GeneratedRegex(@"\b[0-9A-Z]{2}\.[0-9A-Z]{3}\.[0-9A-Z]{3}/[0-9A-Z]{4}-\d{2}\b")]
    private static partial Regex CnpjFormatado();

    [GeneratedRegex(@"\b[0-9A-Z]{12}\d{2}\b")]
    private static partial Regex CnpjSemFormatacao();

    [GeneratedRegex(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b")]
    private static partial Regex Cpf();
}

internal sealed class MascaramentoLogProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord registro)
    {
        if (registro.FormattedMessage is { } mensagem)
        {
            registro.FormattedMessage = MascaradorDadosSensiveis.Mascarar(mensagem);
        }

        if (registro.Body is { } corpo)
        {
            registro.Body = MascaradorDadosSensiveis.Mascarar(corpo);
        }

        registro.Attributes = registro.Attributes?
            .Select(a => a.Value is string valor
                ? new KeyValuePair<string, object?>(a.Key, MascaradorDadosSensiveis.Mascarar(valor))
                : a)
            .ToList();
    }
}
