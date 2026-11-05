using Microsoft.SemanticKernel.ChatCompletion;

namespace Api.Agente.Application.Tests.Fakes;

public class FakeChatCompletionServiceTests
{
    [Fact]
    public async Task Devolve_as_respostas_na_ordem_e_guarda_o_historico()
    {
        var modelo = new FakeChatCompletionService("{ inválido", "{}");
        var historico = new ChatHistory("instruções");

        var primeira = await modelo.GetChatMessageContentsAsync(historico, cancellationToken: TestContext.Current.CancellationToken);
        var segunda = await modelo.GetChatMessageContentsAsync(historico, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("{ inválido", primeira[0].Content);
        Assert.Equal("{}", segunda[0].Content);
        Assert.Equal(2, modelo.Historicos.Count);
    }

    [Fact]
    public async Task Falha_quando_nao_ha_mais_respostas()
    {
        var modelo = new FakeChatCompletionService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            modelo.GetChatMessageContentsAsync(new ChatHistory(), cancellationToken: TestContext.Current.CancellationToken));
    }
}
