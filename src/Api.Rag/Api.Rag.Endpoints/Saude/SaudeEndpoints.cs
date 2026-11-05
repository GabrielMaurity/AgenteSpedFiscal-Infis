using Api.Rag.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Api.Rag.Endpoints.Saude;

public static class SaudeEndpoints
{
    private const string TagProntidao = "prontidao";

    public static IServiceCollection AddSaude(this IServiceCollection services)
    {
        services.AddHealthChecks().AddDbContextCheck<RagDbContext>("postgres", tags: [TagProntidao]);
        return services;
    }

    /// <summary>Sem autenticação e sem dados: a resposta informa só o estado (constitution 5.1).</summary>
    public static IEndpointRouteBuilder MapSaude(this IEndpointRouteBuilder rotas)
    {
        rotas.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        rotas.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(TagProntidao) }).AllowAnonymous();
        return rotas;
    }
}
