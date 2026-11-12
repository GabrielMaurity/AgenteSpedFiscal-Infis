using System.Net;
using System.Text.Json;
using Api.Rag.Endpoints.Infraestrutura;
using Api.Rag.Tests.Infra;

namespace Api.Rag.Tests.Documentacao;

/// <summary>
/// Swagger fora de desenvolvimento (research R-40): desligado por padrão; com a chave ligada, a
/// documentação abre sem token e os endpoints continuam exigindo token.
/// </summary>
public sealed class SwaggerForaDeDevelopmentTests
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static FabricaApiRag Fabrica(string? habilitada)
    {
        var configuracao = FabricaApiRag.ConfiguracaoPadrao();
        if (habilitada is not null)
        {
            configuracao[DocumentacaoApiExtensions.ChaveHabilitada] = habilitada;
        }

        return new FabricaApiRag { Configuracao = configuracao };
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
        Assert.True(documento.RootElement.GetProperty("paths").TryGetProperty("/api/v1/documentos", out _));
        Assert.True(documento.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        // Sem "servers", o Swagger usa a origem da página (https atrás do proxy do Vercel).
        Assert.False(documento.RootElement.TryGetProperty("servers", out var servidores) && servidores.GetArrayLength() > 0);

        var endpoint = await cliente.GetAsync(new Uri("/api/v1/documentos", UriKind.Relative), Cancelamento);
        Assert.Equal(HttpStatusCode.Unauthorized, endpoint.StatusCode);
    }
}
