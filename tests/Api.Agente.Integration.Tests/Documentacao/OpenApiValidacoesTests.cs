using System.Text.Json;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;

namespace Api.Agente.Integration.Tests.Documentacao;

/// <summary>
/// O Swagger mostra o campo do arquivo no envio da EFD (research R-36). O endpoint lê o multipart em
/// fluxo, sem model binding, então o formulário precisa estar declarado nos metadados.
/// </summary>
public sealed class OpenApiValidacoesTests
{
    [Fact]
    public async Task Envio_da_validacao_aparece_como_multipart_com_o_campo_arquivo()
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao[AutenticacaoDesenvolvimentoHandler.ChaveConfiguracao] = "true";
        await using var fabrica = new FabricaApiAgente { Configuracao = configuracao, Ambiente = "Development" };

        using var documento = JsonDocument.Parse(await fabrica.CreateClient().GetStringAsync(
            new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));

        var post = documento.RootElement.GetProperty("paths").GetProperty("/api/v1/validacoes").GetProperty("post");
        var esquema = Resolver(documento.RootElement, post.GetProperty("requestBody").GetProperty("content")
            .GetProperty("multipart/form-data").GetProperty("schema"));
        // O arquivo pode vir em linha ou por referência ao esquema IFormFile.
        Assert.Equal("binary", Resolver(documento.RootElement, esquema.GetProperty("properties").GetProperty("arquivo"))
            .GetProperty("format").GetString());
        Assert.Contains("arquivo", esquema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.True(post.GetProperty("responses").TryGetProperty("202", out _));
    }

    private static JsonElement Resolver(JsonElement raiz, JsonElement esquema)
    {
        if (!esquema.TryGetProperty("$ref", out var referencia))
        {
            return esquema;
        }

        var nome = referencia.GetString()!.Split('/')[^1];
        return raiz.GetProperty("components").GetProperty("schemas").GetProperty(nome);
    }
}
