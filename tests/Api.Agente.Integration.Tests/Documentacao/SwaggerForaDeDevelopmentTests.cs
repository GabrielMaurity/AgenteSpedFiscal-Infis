using System.Net;
using System.Text.Json;
using Api.Agente.Endpoints.Infraestrutura;
using Api.Agente.Integration.Tests.Infra;

namespace Api.Agente.Integration.Tests.Documentacao;

/// <summary>
/// Swagger fora de desenvolvimento (research R-40): desligado por padrão; com a chave ligada, a
/// documentação abre sem token e os endpoints continuam exigindo token.
/// </summary>
public sealed class SwaggerForaDeDevelopmentTests
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static FabricaApiAgente Fabrica(string? habilitada)
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        if (habilitada is not null)
        {
            configuracao[DocumentacaoApiExtensions.ChaveHabilitada] = habilitada;
        }

        return new FabricaApiAgente { Configuracao = configuracao };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task Sem_a_chave_ligada_a_documentacao_nao_e_publicada(string? habilitada)
    {
        await using var fabrica = Fabrica(habilitada);
        var cliente = fabrica.CreateClient();

        foreach (var rota in new[] { "/swagger/index.html", "/openapi/v1.json" })
        {
            var resposta = await cliente.GetAsync(new Uri(rota, UriKind.Relative), Cancelamento);
            Assert.NotEqual(HttpStatusCode.OK, resposta.StatusCode);
        }
    }

    [Fact]
    public async Task Com_a_chave_ligada_a_documentacao_abre_sem_token_e_os_endpoints_exigem_token()
    {
        await using var fabrica = Fabrica("true");
        var cliente = fabrica.CreateClient();

        var tela = await cliente.GetAsync(new Uri("/swagger/index.html", UriKind.Relative), Cancelamento);
        Assert.Equal(HttpStatusCode.OK, tela.StatusCode);

        using var documento = JsonDocument.Parse(await cliente.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Cancelamento));
        Assert.True(documento.RootElement.GetProperty("paths").TryGetProperty("/api/v1/validacoes", out _));
        Assert.True(documento.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        // Sem "servers", o Swagger usa a origem da página (https atrás do proxy do Vercel).
        Assert.False(documento.RootElement.TryGetProperty("servers", out var servidores) && servidores.GetArrayLength() > 0);

        var endpoint = await cliente.GetAsync(new Uri("/api/v1/regras", UriKind.Relative), Cancelamento);
        Assert.Equal(HttpStatusCode.Unauthorized, endpoint.StatusCode);
    }
}
