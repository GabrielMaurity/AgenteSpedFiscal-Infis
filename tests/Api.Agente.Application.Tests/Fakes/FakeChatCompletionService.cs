using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Api.Agente.Application.Tests.Fakes;

/// <summary>Modelo falso com respostas roteirizadas, inclusive inválidas, para a suíte sem OpenAI (P-22).</summary>
public sealed class FakeChatCompletionService : IChatCompletionService
{
    private readonly Queue<ChatMessageContent> _respostas;

    public FakeChatCompletionService(params string[] respostas) =>
        _respostas = new(respostas.Select(r => new ChatMessageContent(AuthorRole.Assistant, r)));

    private FakeChatCompletionService(IEnumerable<ChatMessageContent> respostas) => _respostas = new(respostas);

    /// <summary>Respostas completas, inclusive pedidos de ferramenta (FunctionCallContent).</summary>
    public static FakeChatCompletionService ComMensagens(params ChatMessageContent[] respostas) => new(respostas.AsEnumerable());
    private readonly List<ChatHistory> _historicos = [];

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    /// <summary>Históricos recebidos, para conferir o que foi enviado ao modelo.</summary>
    public IReadOnlyList<ChatHistory> Historicos => _historicos;

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatMessageContent> resultado = [Proxima(chatHistory)];
        return Task.FromResult(resultado);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, Proxima(chatHistory).Content);
        await Task.CompletedTask;
    }

    private ChatMessageContent Proxima(ChatHistory historico)
    {
        _historicos.Add([.. historico]);
        return _respostas.Count > 0
            ? _respostas.Dequeue()
            : throw new InvalidOperationException("O modelo falso não tem mais respostas roteirizadas.");
    }
}
