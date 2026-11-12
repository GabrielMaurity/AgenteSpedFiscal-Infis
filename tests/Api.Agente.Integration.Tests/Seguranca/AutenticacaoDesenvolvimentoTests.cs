using System.Net;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;

namespace Api.Agente.Integration.Tests.Seguranca;

/// <summary>Modo sem autenticação: só em Development e com as mesmas políticas (FR-080).</summary>
public sealed class AutenticacaoDesenvolvimentoTests
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static FabricaApiAgente Fabrica(string ambiente, bool desabilitada)
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao[AutenticacaoDesenvolvimentoHandler.ChaveConfiguracao] = desabilitada ? "true" : "false";
        return new FabricaApiAgente { Configuracao = configuracao, Ambiente = ambiente };
    }

    [Fact]
    public async Task Em_development_com_a_chave_ligada_os_endpoints_dispensam_token()
    {
        await using var fabrica = Fabrica("Development", desabilitada: true);

        var resposta = await fabrica.CreateClient().GetAsync(new Uri("/api/v1/regras", UriKind.Relative), Cancelamento);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Com_a_chave_desligada_continua_exigindo_token()
    {
        await using var fabrica = Fabrica("Development", desabilitada: false);

        var resposta = await fabrica.CreateClient().GetAsync(new Uri("/api/v1/regras", UriKind.Relative), Cancelamento);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task Fora_de_development_a_chave_impede_o_inicio(string ambiente)
    {
        await using var fabrica = Fabrica(ambiente, desabilitada: true);

        var erro = Assert.Throws<InvalidOperationException>(() => fabrica.CreateClient());

        Assert.Contains(AutenticacaoDesenvolvimentoHandler.ChaveConfiguracao, erro.Message, StringComparison.Ordinal);
        await Task.CompletedTask;
    }
}
