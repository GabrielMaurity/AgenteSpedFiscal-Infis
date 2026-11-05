using Api.Agente.Application.TabelasReferencia;
using Api.Agente.Application.Validacao;
using Api.Agente.Domain.Efd;
using Api.Agente.Infrastructure.Arquivos;
using Api.Agente.Infrastructure.Configuracao;
using Api.Agente.Infrastructure.Fila;
using Api.Agente.Infrastructure.TabelasReferencia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Agente.Infrastructure.Validacao;

public static class ValidacaoExtensions
{
    public static IServiceCollection AddValidacaoEfd(this IServiceCollection services)
    {
        // Catálogo inválido impede o início do serviço (FR-030).
        services.AddSingleton(_ => DadosEfd.Carregar());
        services.AddSingleton(provedor => new ParametrosValidacao(
            provedor.GetRequiredService<IOptions<LimitesOpcoes>>().Value.OcorrenciasDetalhadasPorRegra));

        services.AddSingleton<ArmazenamentoTemporario>();
        services.AddSingleton<IArmazenamentoTemporario>(p => p.GetRequiredService<ArmazenamentoTemporario>());
        services.AddSingleton<FilaExecucoes>();
        services.AddSingleton<IFilaExecucoes>(p => p.GetRequiredService<FilaExecucoes>());
        services.AddSingleton<IRepositorioNcm, RepositorioNcm>();
        services.AddSingleton<ImportarNcmHandler>();
        services.AddSingleton<ValidarArquivoHandler>();
        services.AddHostedService<ProcessadorExecucoes>();
        return services;
    }
}
