using System.Net;
using Api.Rag.Tests.Infra;

namespace Api.Rag.Tests.Publicacao;

/// <summary>Com o limite da publicação no Vercel (4 MB), a ingestão recusa com 413 (feature 002, T009, FR-P04).</summary>
public sealed class LimiteVercelTests
{
    [Fact]
    public async Task Documento_acima_de_4_mb_recebe_413_com_problem_json()
    {
        var configuracao = FabricaApiRag.ConfiguracaoPadrao();
        configuracao["Ingestao:TamanhoMaximoArquivoBytes"] = "4000000";
        await using var fabrica = new FabricaApiRag { Configuracao = configuracao };
        var cliente = fabrica.Cliente(FabricaApiRag.CriarToken(papeis: "admin_base"));
        using var corpo = new MultipartFormDataContent
        {
            { new ByteArrayContent(new byte[5 * 1024 * 1024]), "arquivo", "guia.pdf" },
            { new StringContent("oficial"), "classificacao" },
        };

        var resposta = await cliente.PostAsync(new Uri("/api/v1/documentos", UriKind.Relative), corpo, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }
}
