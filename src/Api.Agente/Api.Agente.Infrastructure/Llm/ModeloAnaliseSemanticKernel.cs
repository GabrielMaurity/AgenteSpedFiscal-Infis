using System.Diagnostics;
using Api.Agente.Application.Analise;
using Api.Agente.Application.Consumo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using ChatTokenUsage = OpenAI.Chat.ChatTokenUsage;

namespace Api.Agente.Infrastructure.Llm;

/// <summary>
/// Adaptador do LLM pelo Semantic Kernel. Cada chamada ao modelo gera um registro de consumo, com a
/// tentativa e os tokens informados pelo provedor ou nulos quando desconhecidos (P-17, FR-083). As
/// ferramentas pedidas pelo modelo são executadas aqui, uma a uma, sempre pelo filtro de autorização.
/// </summary>
public sealed partial class ModeloAnaliseSemanticKernel(
    KernelFactory fabrica,
    IServiceScopeFactory escopos,
    TimeProvider relogio,
    ILogger<ModeloAnaliseSemanticKernel> logger) : IModeloAnalise
{
    public const string Provedor = "openai";

    public string Modelo => fabrica.Modelo;

    public async Task<RespostaModelo> GerarAsync(PedidoModelo pedido, CancellationToken cancelamento)
    {
        var kernel = fabrica.Criar(pedido.Sessao);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var historico = new ChatHistory(pedido.InstrucoesSistema);
        foreach (var mensagem in pedido.Mensagens)
        {
            historico.AddMessage(mensagem.DoUsuario ? AuthorRole.User : AuthorRole.Assistant, mensagem.Texto);
        }

        // Cada rodada pode pedir ferramentas; depois do limite, a última rodada exige a resposta final.
        var rodadas = fabrica.LimiteChamadas + 1;
        for (var rodada = 0; rodada <= rodadas; rodada++)
        {
            var resposta = await ChamarAsync(chat, historico, kernel, pedido, permitirFerramentas: rodada < rodadas, cancelamento);
            var chamadas = FunctionCallContent.GetFunctionCalls(resposta).ToList();
            if (chamadas.Count == 0)
            {
                return new RespostaModelo(resposta.Content ?? "");
            }

            historico.Add(resposta);
            foreach (var chamada in chamadas)
            {
                historico.Add((await InvocarAsync(kernel, chamada, pedido.Sessao, cancelamento)).ToChatMessage());
            }
        }

        throw new ModeloIndisponivelException("O modelo não produziu resposta final dentro do limite de rodadas.");
    }

    private async Task<ChatMessageContent> ChamarAsync(
        IChatCompletionService chat, ChatHistory historico, Kernel kernel, PedidoModelo pedido, bool permitirFerramentas, CancellationToken cancelamento)
    {
        var inicio = relogio.GetUtcNow();
        var cronometro = Stopwatch.StartNew();
        try
        {
            var respostas = await chat.GetChatMessageContentsAsync(historico, fabrica.Configuracoes(pedido.Formato, permitirFerramentas), kernel, cancelamento);
            var resposta = respostas.Count > 0 ? respostas[0] : throw new ModeloIndisponivelException("O modelo não devolveu mensagem.");
            var uso = resposta.Metadata?.GetValueOrDefault("Usage") as ChatTokenUsage;
            await RegistrarConsumoAsync(pedido, inicio, cronometro, uso?.InputTokenCount, uso?.OutputTokenCount, "sucesso");
            return resposta;
        }
        catch (Exception excecao) when (excecao is not ModeloIndisponivelException && !cancelamento.IsCancellationRequested)
        {
            await RegistrarConsumoAsync(pedido, inicio, cronometro, null, null, "falha");
            Log.FalhaModelo(logger, excecao, pedido.Sessao.CorrelacaoId);
            throw new ModeloIndisponivelException("O provedor do modelo não respondeu.", excecao);
        }
    }

    /// <summary>Ferramenta inexistente vira erro para o modelo e é registrada como negada.</summary>
    private static async Task<FunctionResultContent> InvocarAsync(Kernel kernel, FunctionCallContent chamada, SessaoFerramentas sessao, CancellationToken cancelamento)
    {
        if (!kernel.Plugins.TryGetFunction(chamada.PluginName, chamada.FunctionName, out _))
        {
            const string Motivo = "Ferramenta inexistente.";
            sessao.Registrar(new ChamadaFerramenta(chamada.FunctionName, false, Motivo, 0));
            return new FunctionResultContent(chamada, $"{FiltroAutorizacaoFerramentas.PrefixoNegacao} {Motivo}");
        }

        return await chamada.InvokeAsync(kernel, cancelamento);
    }

    private async Task RegistrarConsumoAsync(PedidoModelo pedido, DateTimeOffset inicio, Stopwatch cronometro, int? entrada, int? saida, string status)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            await escopo.ServiceProvider.GetRequiredService<IRegistradorConsumo>().RegistrarAsync(
                new RegistroConsumo(
                    pedido.Sessao.IdConsumo,
                    pedido.Sessao.CorrelacaoId,
                    pedido.Sessao.OrgId,
                    Provedor,
                    Modelo,
                    "chat",
                    pedido.Tentativa,
                    entrada,
                    saida,
                    inicio,
                    (int)cronometro.ElapsedMilliseconds,
                    status),
                CancellationToken.None);
        }
        catch (Exception excecao)
        {
            // A falha do registro não derruba a análise, mas fica visível nos logs (P-17).
            Log.FalhaConsumo(logger, excecao, pedido.Sessao.CorrelacaoId);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Falha na chamada ao modelo, correlação {CorrelacaoId}.")]
        public static partial void FalhaModelo(ILogger logger, Exception excecao, string correlacaoId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao registrar o consumo de IA, correlação {CorrelacaoId}.")]
        public static partial void FalhaConsumo(ILogger logger, Exception excecao, string correlacaoId);
    }
}
