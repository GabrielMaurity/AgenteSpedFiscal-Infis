using Api.Agente.Infrastructure.Configuracao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Api.Agente.Infrastructure.Resiliencia;

public static class ClientesHttp
{
    public const string OpenAi = "openai";
    public const string McpRag = "mcp-rag";
}

public static class ResilienciaExtensions
{
    /// <summary>Timeout por tentativa, retentativas com backoff e jitter, e circuit breaker (research R-22).</summary>
    public static IServiceCollection AddClientesResilientes(this IServiceCollection services)
    {
        services.AddHttpClient(ClientesHttp.OpenAi).AddStandardResilienceHandler()
            .Configure((estrategia, provedor) =>
            {
                var opcoes = provedor.GetRequiredService<IOptions<ResilienciaOpcoes>>().Value;
                Aplicar(estrategia, opcoes, TimeSpan.FromSeconds(opcoes.TimeoutLlmSegundos));
            });

        services.AddHttpClient(ClientesHttp.McpRag).AddStandardResilienceHandler()
            .Configure((estrategia, provedor) =>
            {
                var opcoes = provedor.GetRequiredService<IOptions<ResilienciaOpcoes>>().Value;
                Aplicar(estrategia, opcoes, TimeSpan.FromSeconds(opcoes.TimeoutMcpSegundos));
            });

        return services;
    }

    private static void Aplicar(HttpStandardResilienceOptions estrategia, ResilienciaOpcoes opcoes, TimeSpan timeoutPorTentativa)
    {
        estrategia.AttemptTimeout.Timeout = timeoutPorTentativa;
        estrategia.Retry.MaxRetryAttempts = opcoes.Retentativas;
        estrategia.Retry.BackoffType = DelayBackoffType.Exponential;
        estrategia.Retry.UseJitter = true;
        estrategia.Retry.Delay = TimeSpan.FromMilliseconds(opcoes.AtrasoBaseMilissegundos);

        // O pipeline padrão exige janela do circuit breaker de ao menos 2x o timeout por tentativa,
        // e tempo total maior que o timeout por tentativa.
        estrategia.CircuitBreaker.SamplingDuration = timeoutPorTentativa * 2;
        estrategia.TotalRequestTimeout.Timeout = (timeoutPorTentativa * (opcoes.Retentativas + 1)) + TimeSpan.FromSeconds(30);
    }
}
