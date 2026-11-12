using System.Text.Json;
using Api.Rag.Endpoints.Seguranca;
using Api.Rag.Tests.Infra;

namespace Api.Rag.Tests.Documentacao;

/// <summary>O Swagger mostra o envio do arquivo e dos campos da ingestão (research R-36).</summary>
public sealed class OpenApiTests
{
    [Fact]
    public async Task Ingestao_aparece_como_formulario_multipart_com_arquivo_e_campos()
    {
        var configuracao = FabricaApiRag.ConfiguracaoPadrao();
        configuracao[AutenticacaoDesenvolvimentoHandler.ChaveConfiguracao] = "true";
        await using var fabrica = new FabricaApiRag { Configuracao = configuracao, Ambiente = "Development" };

        using var documento = JsonDocument.Parse(await fabrica.CreateClient().GetStringAsync(
            new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));

        var corpo = documento.RootElement.GetProperty("paths").GetProperty("/api/v1/documentos").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema");
        var esquema = Resolver(documento.RootElement, corpo);
        var propriedades = esquema.GetProperty("properties");
        // O arquivo pode vir em linha ou por referência ao esquema IFormFile.
        Assert.Equal("binary", Resolver(documento.RootElement, propriedades.GetProperty("arquivo")).GetProperty("format").GetString());
        foreach (var campo in new[] { "classificacao", "tipoDocumento", "titulo", "origem", "versao", "vigenciaInicio", "vigenciaFim" })
        {
            Assert.True(propriedades.TryGetProperty(campo, out _), campo);
        }

        Assert.Contains("arquivo", esquema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
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
