using System.Net;
using System.Net.Http.Headers;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;

namespace Api.Agente.Integration.Tests.Publicacao;

/// <summary>Com o limite da publicação no Vercel (4 MB), a própria API recusa com 413 (feature 002, T009, FR-P04).</summary>
public sealed class LimiteVercelTests
{
    [Fact]
    public async Task Arquivo_acima_de_4_mb_recebe_413_com_problem_json()
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao["Limites:TamanhoMaximoArquivoBytes"] = "4000000";
        await using var fabrica = new FabricaApiAgente { Configuracao = configuracao };
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FabricaApiAgente.CriarToken(papeis: Papeis.Analista));
        using var corpo = new MultipartFormDataContent { { new ByteArrayContent(new byte[6 * 1024 * 1024]), "arquivo", "efd.txt" } };

        var resposta = await cliente.PostAsync(new Uri("/api/v1/validacoes", UriKind.Relative), corpo, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }
}
