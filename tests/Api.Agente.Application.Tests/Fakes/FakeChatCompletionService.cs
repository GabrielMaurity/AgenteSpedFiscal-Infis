using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Api.Agente.Application.Tests.Fakes;

/// <summary>Modelo falso com respostas roteirizadas, inclusive inválidas, para a suíte sem OpenAI (P-22).</summary>
public sealed class FakeChatCompletionService(params string[] respostas) : IChatCompletionService
{
    private readonly Queue<string> _respostas = new(respostas);
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
        IReadOnlyList<ChatMessageContent> resultado = [new(AuthorRole.Assistant, Proxima(chatHistory))];
        return Task.FromResult(resultado);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, Proxima(chatHistory));
        await Task.CompletedTask;
    }

    private string Proxima(ChatHistory historico)
    {
        _historicos.Add(historico);
        return _respostas.Count > 0
            ? _respostas.Dequeue()
            : throw new InvalidOperationException("O modelo falso não tem mais respostas roteirizadas.");
    }
}
