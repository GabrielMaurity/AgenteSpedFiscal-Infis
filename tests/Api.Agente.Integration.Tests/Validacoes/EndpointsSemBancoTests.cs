using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;

namespace Api.Agente.Integration.Tests.Validacoes;

/// <summary>Autorização e recusas que acontecem antes de qualquer acesso aos bancos.</summary>
public sealed class EndpointsSemBancoTests(FabricaApiAgente fabrica) : IClassFixture<FabricaApiAgente>
{
    private static readonly Uri Rota = new("/api/v1/validacoes", UriKind.Relative);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private HttpClient Cliente(string? token = null)
    {
        var cliente = fabrica.CreateClient();
        if (token is not null)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
    }

    private static MultipartFormDataContent Multipart(byte[] conteudo, string nome)
    {
        var corpo = new MultipartFormDataContent { { new ByteArrayContent(conteudo), "arquivo", nome } };
        return corpo;
    }

    [Fact]
    public async Task Sem_token_recebe_401()
    {
        var resposta = await Cliente().PostAsync(Rota, Multipart([65], "a.txt"), Cancelamento);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Papel_sem_permissao_recebe_403()
    {
        var resposta = await Cliente(FabricaApiAgente.CriarToken(papeis: Papeis.AdminBase)).PostAsync(Rota, Multipart([65], "a.txt"), Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Listagem_e_negada_ao_agente_externo()
    {
        var resposta = await Cliente(FabricaApiAgente.CriarToken(papeis: Papeis.AgenteExterno)).GetAsync(Rota, Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Corpo_que_nao_e_multipart_recebe_415()
    {
        var token = FabricaApiAgente.CriarToken(papeis: Papeis.Analista);

        var resposta = await Cliente(token).PostAsync(Rota, new StringContent("|0000|", Encoding.Latin1, "text/plain"), Cancelamento);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resposta.StatusCode);
    }

    [Theory]
    [InlineData("efd.pdf", new byte[] { 65 })]
    [InlineData("efd.txt", new byte[] { 0xEF, 0xBB, 0xBF, 65 })]
    [InlineData("efd.txt", new byte[] { 0xFF, 0xFE, 65, 0 })]
    [InlineData("efd.txt", new byte[] { 65, 0, 66 })]
    [InlineData("efd.txt", new byte[0])]
    public async Task Extensao_codificacao_ou_conteudo_invalidos_recebem_415(string nome, byte[] conteudo)
    {
        var token = FabricaApiAgente.CriarToken(papeis: Papeis.Analista);

        var resposta = await Cliente(token).PostAsync(Rota, Multipart(conteudo, nome), Cancelamento);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Multipart_sem_o_campo_arquivo_recebe_400()
    {
        var token = FabricaApiAgente.CriarToken(papeis: Papeis.Analista);
        var corpo = new MultipartFormDataContent { { new StringContent("x"), "contexto" } };

        var resposta = await Cliente(token).PostAsync(Rota, corpo, Cancelamento);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Arquivo_acima_do_limite_recebe_413()
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao["Limites:TamanhoMaximoArquivoBytes"] = "10";
        await using var limitada = new FabricaApiAgente { Configuracao = configuracao };
        var cliente = limitada.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FabricaApiAgente.CriarToken(papeis: Papeis.Analista));

        var resposta = await cliente.PostAsync(Rota, Multipart(new byte[11], "a.txt"), Cancelamento);

        // O conteúdo com bytes nulos seria 415; aqui o tamanho é verificado depois da inspeção inicial.
        Assert.Contains(resposta.StatusCode, new[] { HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.UnsupportedMediaType });

        var texto = await cliente.PostAsync(Rota, Multipart(Encoding.ASCII.GetBytes("abcdefghijk"), "a.txt"), Cancelamento);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, texto.StatusCode);
    }

    [Fact]
    public async Task Excesso_de_requisicoes_da_organizacao_recebe_429()
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao["Limites:RequisicoesPorJanela"] = "1";
        configuracao["Limites:JanelaSegundos"] = "3600";
        await using var limitada = new FabricaApiAgente { Configuracao = configuracao };
        var cliente = limitada.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FabricaApiAgente.CriarToken(papeis: Papeis.Analista));

        var primeira = await cliente.PostAsync(Rota, new StringContent("x"), Cancelamento);
        var segunda = await cliente.PostAsync(Rota, new StringContent("x"), Cancelamento);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, segunda.StatusCode);

        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FabricaApiAgente.CriarToken(orgId: "outra", papeis: Papeis.Analista));
        var outraOrganizacao = await cliente.PostAsync(Rota, new StringContent("x"), Cancelamento);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, outraOrganizacao.StatusCode);
    }
}
