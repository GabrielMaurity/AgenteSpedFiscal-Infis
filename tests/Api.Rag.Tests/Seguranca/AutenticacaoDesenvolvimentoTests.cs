using System.Net;
using System.Text;
using Api.Rag.Endpoints.Seguranca;
using Api.Rag.Tests.Infra;

namespace Api.Rag.Tests.Seguranca;

/// <summary>Modo sem autenticação: só em Development; o /mcp aceita a Api.Agente local sem token.</summary>
public sealed class AutenticacaoDesenvolvimentoTests
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static FabricaApiRag Fabrica(string ambiente)
    {
        var configuracao = FabricaApiRag.ConfiguracaoPadrao();
        configuracao[AutenticacaoDesenvolvimentoHandler.ChaveConfiguracao] = "true";
        return new FabricaApiRag { Configuracao = configuracao, Ambiente = ambiente };
    }

    [Fact]
    public async Task Em_development_o_mcp_responde_sem_token()
    {
        await using var fabrica = Fabrica("Development");
        using var corpo = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");

        var resposta = await cliente.PostAsync(new Uri("/mcp", UriKind.Relative), corpo, Cancelamento);

        Assert.NotEqual(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Fora_de_development_a_chave_impede_o_inicio()
    {
        await using var fabrica = Fabrica("Production");

        Assert.Throws<InvalidOperationException>(() => fabrica.CreateClient());
        await Task.CompletedTask;
    }
}
