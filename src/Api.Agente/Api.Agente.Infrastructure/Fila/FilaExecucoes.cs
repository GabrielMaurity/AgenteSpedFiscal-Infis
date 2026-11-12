using System.Threading.Channels;
using Api.Agente.Application.Validacao;
using Api.Agente.Infrastructure.Arquivos;
using Api.Agente.Infrastructure.Configuracao;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Agente.Infrastructure.Fila;

/// <summary>Fila em memória: o arquivo não é retido, então não há o que recuperar após reinício (research R-04).</summary>
public sealed class FilaExecucoes : IFilaExecucoes
{
    private readonly Channel<ItemFila> _canal = Channel.CreateUnbounded<ItemFila>(new UnboundedChannelOptions { SingleReader = false });

    public ChannelReader<ItemFila> Leitor => _canal.Reader;

    public ValueTask EnfileirarAsync(ItemFila item, CancellationToken cancelamento) => _canal.Writer.WriteAsync(item, cancelamento);
}

public sealed class ProcessadorExecucoes(
    FilaExecucoes fila,
    ValidarArquivoHandler handler,
    ArmazenamentoTemporario armazenamento,
    IOptions<LimitesOpcoes> limites,
    IOptions<RecuperacaoOpcoes> recuperacao,
    ILogger<ProcessadorExecucoes> logger) : BackgroundService
{
    private static readonly TimeSpan IntervaloTentativas = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecuperarInterrompidasAsync(stoppingToken);

        var tarefas = Enumerable.Range(0, limites.Value.ProcessamentosParalelos)
            .Select(_ => ConsumirAsync(stoppingToken))
            .ToList();
        if (!recuperacao.Value.InstanciaUnica)
        {
            tarefas.Add(VarrerAsync(stoppingToken));
        }

        await Task.WhenAll(tarefas);
    }

    /// <summary>Com várias instâncias, encerra periodicamente as execuções de instâncias que pararam.</summary>
    private async Task VarrerAsync(CancellationToken cancelamento)
    {
        using var intervalo = new PeriodicTimer(TimeSpan.FromMinutes(recuperacao.Value.IntervaloVarreduraMinutos));
        while (await intervalo.WaitForNextTickAsync(cancelamento))
        {
            try
            {
                await handler.FalharEmAndamentoAsync(recuperacao.Value.Limiar, cancelamento);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                logger.LogWarning(excecao, "Falha na varredura de execuções interrompidas.");
            }
        }
    }

    private async Task RecuperarInterrompidasAsync(CancellationToken cancelamento)
    {
        while (!cancelamento.IsCancellationRequested)
        {
            try
            {
                armazenamento.LimparDiretorio();
                await handler.FalharEmAndamentoAsync(recuperacao.Value.Limiar, cancelamento);
                return;
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                logger.LogWarning(excecao, "Não foi possível revisar execuções interrompidas; nova tentativa em {Intervalo}.", IntervaloTentativas);
                await Task.Delay(IntervaloTentativas, cancelamento);
            }
        }
    }

    private async Task ConsumirAsync(CancellationToken cancelamento)
    {
        await foreach (var item in fila.Leitor.ReadAllAsync(cancelamento))
        {
            try
            {
                await handler.ProcessarAsync(item, cancelamento);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                logger.LogError(excecao, "Falha ao processar a execução {ExecucaoId}.", item.ExecucaoId);
            }
        }
    }
}
