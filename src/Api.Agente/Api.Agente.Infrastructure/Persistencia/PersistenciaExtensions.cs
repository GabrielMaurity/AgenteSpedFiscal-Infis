using Api.Agente.Application.Consumo;
using Api.Agente.Infrastructure.Configuracao;
using Api.Agente.Infrastructure.Consumo;
using Api.Agente.Infrastructure.Persistencia.Mongo;
using Api.Agente.Infrastructure.Persistencia.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Api.Agente.Infrastructure.Persistencia;

public static class PersistenciaExtensions
{
    public static IServiceCollection AddPersistenciaAgente(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMongoAgente();
        services.AddDbContext<AgenteDbContext>((provedor, opcoes) =>
            opcoes.UseNpgsql(provedor.GetRequiredService<IOptions<PostgresOpcoes>>().Value.ConnectionString));
        services.AddScoped<IRegistradorConsumo, RegistradorConsumo>();
        services.AddHostedService<InicializadorBancos>();
        return services;
    }
}

/// <summary>
/// Prepara os bancos em segundo plano, com novas tentativas, para não impedir o início do serviço;
/// /health/ready informa a indisponibilidade. Migrações só rodam quando habilitadas explicitamente.
/// </summary>
public sealed class InicializadorBancos(
    IServiceScopeFactory escopos,
    IMongoDatabase mongo,
    IOptions<PersistenciaOpcoes> opcoes,
    ILogger<InicializadorBancos> logger) : BackgroundService
{
    private static readonly TimeSpan IntervaloTentativas = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MongoExtensions.CriarIndicesAsync(mongo, stoppingToken);

                await using var escopo = escopos.CreateAsyncScope();
                var banco = escopo.ServiceProvider.GetRequiredService<AgenteDbContext>();
                if (opcoes.Value.AplicarMigracoesNaInicializacao)
                {
                    await banco.Database.MigrateAsync(stoppingToken);
                }

                await CarregadorTabelaPrecos.CarregarAsync(banco, CarregadorTabelaPrecos.LerArquivo(), stoppingToken);
                return;
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                logger.LogWarning(excecao, "Bancos ainda não disponíveis; nova tentativa em {Intervalo}.", IntervaloTentativas);
                await Task.Delay(IntervaloTentativas, stoppingToken);
            }
        }
    }
}
