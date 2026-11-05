using Microsoft.Extensions.DependencyInjection;

namespace Api.Agente.Infrastructure.Configuracao;

public static class ConfiguracaoExtensions
{
    /// <summary>Registra as opções e impede o início do serviço com configuração inválida (P-19).</summary>
    public static IServiceCollection AddOpcoesAgente(this IServiceCollection services)
    {
        Registrar<OpenAiOpcoes>(services, OpenAiOpcoes.Secao);
        Registrar<MongoDbOpcoes>(services, MongoDbOpcoes.Secao);
        Registrar<PostgresOpcoes>(services, PostgresOpcoes.Secao);
        Registrar<PersistenciaOpcoes>(services, PersistenciaOpcoes.Secao);
        Registrar<McpRagOpcoes>(services, McpRagOpcoes.Secao);
        Registrar<ArquivosOpcoes>(services, ArquivosOpcoes.Secao);
        Registrar<ResilienciaOpcoes>(services, ResilienciaOpcoes.Secao);
        Registrar<LimitesOpcoes>(services, LimitesOpcoes.Secao);
        return services;
    }

    private static void Registrar<T>(IServiceCollection services, string secao)
        where T : class =>
        services.AddOptions<T>().BindConfiguration(secao).ValidateDataAnnotations().ValidateOnStart();
}
