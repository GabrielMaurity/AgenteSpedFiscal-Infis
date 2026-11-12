using System.Runtime.CompilerServices;
using System.Text.Json;
using Api.Agente.Application.Analise;
using Api.Agente.Domain.Analise;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Api.Agente.Integration.Tests.Infra;

/// <summary>Base de informação em memória, no lugar da Api.Rag.</summary>
public sealed class BaseFalsa : IBaseInformacao
{
    public static readonly TrechoBase Oficial = new(
        "11111111-1111-1111-1111-111111111111", "No exercício de 2026, os valores de IBS e CBS não integram o VL_DOC.",
        ClassificacaoFonte.Oficial, "Guia Prático EFD ICMS/IPI", "3.2.4", "Cap. I, Seção 10", Similaridade: 0.82);

    public static readonly TrechoBase Interno = new(
        "22222222-2222-2222-2222-222222222222", "FAQ interno: confira o C190 antes de transmitir.",
        ClassificacaoFonte.Interno, "FAQ interno Infis", "1", "Pergunta 3", Similaridade: 0.41);

    public List<TrechoBase> Trechos { get; } = [Oficial, Interno];

    public Task<ResultadoBusca> BuscarAsync(ConsultaBase consulta, ContextoRecuperacao contexto, CancellationToken cancelamento)
    {
        contexto.Registrar(Trechos);
        return Task.FromResult(new ResultadoBusca([.. Trechos], Trechos.Count == 0));
    }

    public Task<TrechoBase?> ObterTrechoAsync(string trechoId, string correlacaoId, ContextoRecuperacao contexto, CancellationToken cancelamento) =>
        Task.FromResult(Trechos.FirstOrDefault(t => t.TrechoId == trechoId));
}

/// <summary>Chat falso: a resposta é calculada a partir do histórico recebido, que fica guardado.</summary>
public sealed class ChatFalso(Func<ChatHistory, string> responder) : IChatCompletionService
{
    private readonly List<ChatHistory> _historicos = [];

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public IReadOnlyList<ChatHistory> Historicos
    {
        get
        {
            lock (_historicos)
            {
                return [.. _historicos];
            }
        }
    }

    /// <summary>Análise vazia e válida para as validações; respostas de conversa pelo roteiro.</summary>
    public static ChatFalso Conversa(Func<ChatHistory, string> conversa) =>
        new(historico => historico[0].Content == ConstrutorPrompt.InstrucoesAnalise
            ? """{"versaoContrato":"1.0.0","resumo":"r","explicacoes":[],"indicios":[],"limitacoes":[],"revisaoHumanaRecomendada":false}"""
            : conversa(historico));

    public static string RespostaConversa(string resposta, bool fundamentada, bool dependeDoArquivo, params string[] trechoIds) =>
        JsonSerializer.Serialize(new { versaoContrato = "1.0.0", resposta, fundamentada, dependeDoArquivo, trechoIds, limitacoes = Array.Empty<string>() });

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        lock (_historicos)
        {
            _historicos.Add([.. chatHistory]);
        }

        IReadOnlyList<ChatMessageContent> resultado = [new(AuthorRole.Assistant, responder(chatHistory))];
        return Task.FromResult(resultado);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, responder(chatHistory));
        await Task.CompletedTask;
    }
}

public static class ServicosIa
{
    public static Action<IServiceCollection> Substituir(IBaseInformacao baseInformacao, IChatCompletionService chat) => servicos =>
    {
        servicos.RemoveAll<IBaseInformacao>();
        servicos.AddSingleton(baseInformacao);
        servicos.RemoveAll<IChatCompletionService>();
        servicos.AddSingleton(chat);
    };
}
