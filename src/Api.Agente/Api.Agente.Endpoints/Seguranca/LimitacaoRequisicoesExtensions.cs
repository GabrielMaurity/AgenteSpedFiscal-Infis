using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Api.Agente.Application.Validacao;
using Api.Agente.Infrastructure.Configuracao;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Api.Agente.Endpoints.Seguranca;

public static class LimitacaoRequisicoesExtensions
{
    public const string PoliticaPorOrganizacao = "por-organizacao";

    /// <summary>Janela fixa por org_id em validação e conversa; excedente recebe 429 (research R-26).</summary>
    public static IServiceCollection AddLimitacaoRequisicoes(this IServiceCollection services)
    {
        services.AddSingleton<ILimitadorExecucoes, LimitadorExecucoesPorOrganizacao>();

        services.AddOptions<RateLimiterOptions>().Configure<IOptions<LimitesOpcoes>>((opcoes, limites) =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opcoes.AddPolicy(PoliticaPorOrganizacao, contexto =>
                RateLimitPartition.GetFixedWindowLimiter(
                    contexto.User.FindFirst(AutenticacaoExtensions.ClaimOrganizacao)?.Value ?? "sem-organizacao",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limites.Value.RequisicoesPorJanela,
                        Window = TimeSpan.FromSeconds(limites.Value.JanelaSegundos),
                        QueueLimit = 0,
                    }));
        });
        services.AddRateLimiter(_ => { });

        return services;
    }
}

public sealed class LimitadorExecucoesPorOrganizacao(IOptions<LimitesOpcoes> limites) : ILimitadorExecucoes
{
    private readonly ConcurrentDictionary<string, int> _emAndamento = new();

    public bool TentarReservar(string orgId)
    {
        var maximo = limites.Value.ExecucoesSimultaneasPorOrganizacao;

        while (true)
        {
            var atual = _emAndamento.GetOrAdd(orgId, 0);
            if (atual >= maximo)
            {
                return false;
            }

            if (_emAndamento.TryUpdate(orgId, atual + 1, atual))
            {
                return true;
            }
        }
    }

    public void Liberar(string orgId) =>
        _emAndamento.AddOrUpdate(orgId, 0, (_, atual) => Math.Max(0, atual - 1));
}
