using System.Net.Http.Headers;
using System.Threading.Channels;
using Api.Rag.Application.Base;
using Api.Rag.Application.Busca;
using Api.Rag.Application.Ingestao;
using Api.Rag.Domain.Segmentacao;
using Api.Rag.Infrastructure.Busca;
using Api.Rag.Infrastructure.Configuracao;
using Api.Rag.Infrastructure.Embeddings;
using Api.Rag.Infrastructure.Extracao;
using Api.Rag.Infrastructure.Persistencia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Rag.Infrastructure.Ingestao;

/// <summary>Fila em memória: a durabilidade vem do status gravado, retomado no início do serviço.</summary>
public sealed class FilaIngestao : IFilaIngestao
{
    private readonly Channel<ItemIngestao> _canal = Channel.CreateUnbounded<ItemIngestao>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<ItemIngestao> Leitor => _canal.Reader;

    public ValueTask EnfileirarAsync(ItemIngestao item, CancellationToken cancelamento) => _canal.Writer.WriteAsync(item, cancelamento);
}

/// <summary>Processa a fila um documento por vez; no início, retoma os que ficaram recebidos ou em processamento.</summary>
public sealed partial class ProcessadorIngestao(
    FilaIngestao fila,
    IServiceScopeFactory escopos,
    IOptions<RecuperacaoOpcoes> recuperacao,
    TimeProvider relogio,
    ILogger<ProcessadorIngestao> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RetomarPendentesAsync(stoppingToken);

        await foreach (var item in fila.Leitor.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var escopo = escopos.CreateAsyncScope();
                await escopo.ServiceProvider.GetRequiredService<IngestaoHandler>().ProcessarAsync(item, stoppingToken);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                // O documento já ficou em falha; o detalhe vai só para o log, nunca para a resposta.
                LogFalha(logger, excecao, item.DocumentoId, item.CorrelacaoId);
            }
        }
    }

    private async Task RetomarPendentesAsync(CancellationToken cancelamento)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            // IngeridoEm marca o início da ingestão; com o limiar acima da duração máxima da plataforma, um
            // documento ainda pendente depois dele foi interrompido. O reprocessamento de documento antigo
            // pode ser retomado em paralelo: risco aceito e registrado na feature 002.
            DateTimeOffset? ingeridosAntesDe = recuperacao.Value.Limiar is { } limiar ? relogio.GetUtcNow() - limiar : null;
            foreach (var id in await escopo.ServiceProvider.GetRequiredService<IRepositorioDocumentos>().ListarPendentesAsync(ingeridosAntesDe, cancelamento))
            {
                await fila.EnfileirarAsync(new ItemIngestao(id, $"retomada-{id:N}"), cancelamento);
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            LogRetomada(logger, excecao);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao processar o documento {DocumentoId} (correlação {CorrelacaoId}).")]
    private static partial void LogFalha(ILogger logger, Exception excecao, Guid documentoId, string correlacaoId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível retomar as ingestões pendentes; o banco pode estar indisponível.")]
    private static partial void LogRetomada(ILogger logger, Exception excecao);
}

public static class BaseInformacaoExtensions
{
    /// <summary>Ingestão, busca e embeddings da base de informação (US4).</summary>
    public static IServiceCollection AddBaseInformacao(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(p => new ParametrosSegmentacao(p.GetRequiredService<IOptions<SegmentacaoOpcoes>>().Value.LimiteTokens));
        services.AddSingleton(p => new ParametrosBusca(p.GetRequiredService<IOptions<BuscaOpcoes>>().Value.TopK));

        services.AddScoped<IRepositorioDocumentos, RepositorioDocumentos>();
        services.AddScoped<IBuscaTrechos, BuscaHibrida>();
        services.AddSingleton<IExtratorDocumento, ExtratorDocumento>();
        services.AddHttpClient<IGeradorEmbeddings, GeradorEmbeddings>((provedor, cliente) =>
        {
            var openAi = provedor.GetRequiredService<IOptions<OpenAiOpcoes>>().Value;
            cliente.BaseAddress = new Uri(openAi.Endpoint.EndsWith('/') ? openAi.Endpoint : openAi.Endpoint + "/");
            cliente.Timeout = TimeSpan.FromSeconds(openAi.TimeoutSegundos);
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAi.ApiKey);
        });

        services.AddSingleton<FilaIngestao>();
        services.AddSingleton<IFilaIngestao>(p => p.GetRequiredService<FilaIngestao>());
        services.AddHostedService<ProcessadorIngestao>();
        services.AddScoped<IngestaoHandler>();
        services.AddScoped<ConsultaBase>();
        return services;
    }
}
