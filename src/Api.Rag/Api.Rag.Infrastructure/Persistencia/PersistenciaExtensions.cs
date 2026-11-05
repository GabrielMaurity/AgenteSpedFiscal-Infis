using Api.Rag.Application.Consumo;
using Api.Rag.Infrastructure.Configuracao;
using Api.Rag.Infrastructure.Consumo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Rag.Infrastructure.Persistencia;

public static class PersistenciaExtensions
{
    public static IServiceCollection AddPersistenciaRag(this IServiceCollection services)
    {
        services.AddDbContext<RagDbContext>((provedor, opcoes) => opcoes.UseNpgsql(
            provedor.GetRequiredService<IOptions<PostgresOpcoes>>().Value.ConnectionString,
            npgsql => npgsql.UseVector()));
        services.AddScoped<IRegistradorConsumoEmbeddings, RegistradorConsumoEmbeddings>();
        services.AddHostedService<InicializadorBanco>();
        return services;
    }
}

/// <summary>Aplica migrações em segundo plano, só quando habilitado explicitamente.</summary>
public sealed class InicializadorBanco(
    IServiceScopeFactory escopos,
    IOptions<PersistenciaOpcoes> opcoes,
    ILogger<InicializadorBanco> logger) : BackgroundService
{
    private static readonly TimeSpan IntervaloTentativas = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opcoes.Value.AplicarMigracoesNaInicializacao)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var escopo = escopos.CreateAsyncScope();
                await escopo.ServiceProvider.GetRequiredService<RagDbContext>().Database.MigrateAsync(stoppingToken);
                return;
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                logger.LogWarning(excecao, "Banco ainda não disponível; nova tentativa em {Intervalo}.", IntervaloTentativas);
                await Task.Delay(IntervaloTentativas, stoppingToken);
            }
        }
    }
}
