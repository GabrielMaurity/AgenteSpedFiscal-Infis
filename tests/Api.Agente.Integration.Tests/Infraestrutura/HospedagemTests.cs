using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Api.Agente.Infrastructure.Configuracao;
using Api.Agente.Integration.Tests.Infra;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Agente.Integration.Tests.Infraestrutura;

public sealed class HospedagemTests(FabricaApiAgente fabrica) : IClassFixture<FabricaApiAgente>
{
    private const string Cabecalho = "X-Correlation-Id";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_live_responde_sem_autenticacao_e_sem_dados()
    {
        var resposta = await fabrica.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative), Cancelamento);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Healthy", await resposta.Content.ReadAsStringAsync(Cancelamento));
    }

    [Fact]
    public async Task Health_ready_informa_indisponibilidade_dos_bancos_sem_detalhes()
    {
        var resposta = await fabrica.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancelamento);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Equal("Unhealthy", await resposta.Content.ReadAsStringAsync(Cancelamento));
    }

    [Fact]
    public async Task Correlacao_valida_e_devolvida()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add(Cabecalho, "abc-123");

        var resposta = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative), Cancelamento);

        Assert.Equal("abc-123", resposta.Headers.GetValues(Cabecalho).Single());
    }

    [Fact]
    public async Task Correlacao_ausente_e_gerada()
    {
        var resposta = await fabrica.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative), Cancelamento);

        Assert.False(string.IsNullOrWhiteSpace(resposta.Headers.GetValues(Cabecalho).Single()));
    }

    [Theory]
    [InlineData("com espaço")]
    [InlineData("quebra\r\nde-linha")]
    public void Correlacao_insegura_e_substituida(string recebido)
    {
        var normalizado = Endpoints.Infraestrutura.CorrelacaoMiddleware.Normalizar(recebido);

        Assert.NotEqual(recebido, normalizado);
        Assert.Equal(32, normalizado.Length);
    }

    [Fact]
    public void Correlacao_muito_longa_e_substituida()
    {
        var normalizado = Endpoints.Infraestrutura.CorrelacaoMiddleware.Normalizar(new string('a', 129));

        Assert.Equal(32, normalizado.Length);
    }

    private async Task<HttpResponseMessage> RotaInexistente(string? token)
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add(Cabecalho, "erro-1");
        if (token is not null)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await cliente.GetAsync(new Uri("/nao-existe", UriKind.Relative), Cancelamento);
    }

    [Fact]
    public async Task Sem_token_a_resposta_e_401_em_rfc_9457_com_correlacao()
    {
        var resposta = await RotaInexistente(null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
        Assert.Equal("erro-1", corpo.RootElement.GetProperty("correlacaoId").GetString());
        Assert.Equal(401, corpo.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Token_valido_passa_pela_autenticacao()
    {
        var resposta = await RotaInexistente(FabricaApiAgente.CriarToken());

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Token_sem_org_id_recebe_403()
    {
        var resposta = await RotaInexistente(FabricaApiAgente.CriarToken(orgId: null));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Token_de_outra_audiencia_recebe_401()
    {
        var resposta = await RotaInexistente(FabricaApiAgente.CriarToken(audiencia: "api-rag"));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Theory]
    [InlineData("OpenAi:ApiKey")]
    [InlineData("OpenAi:ModeloAnalise")]
    [InlineData("MongoDb:ConnectionString")]
    [InlineData("MongoDb:Database")]
    [InlineData("Postgres:ConnectionString")]
    [InlineData("McpRag:Url")]
    public void Configuracao_ausente_impede_o_inicio(string chave)
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao.Remove(chave);
        AssertInicioRecusado(configuracao);
    }

    [Theory]
    [InlineData("Limites:TamanhoMaximoArquivoBytes", "0")]
    [InlineData("Resiliencia:Retentativas", "0")]
    public void Configuracao_fora_do_intervalo_impede_o_inicio(string chave, string valor)
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao[chave] = valor;
        AssertInicioRecusado(configuracao);
    }

    // Validação direta: o que ValidateOnStart executa no início, sem a corrida de subir um host que falha.
    private static void AssertInicioRecusado(Dictionary<string, string?> configuracao)
    {
        var servicos = new ServiceCollection();
        servicos.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuracao).Build());
        servicos.AddOpcoesAgente();
        using var provedor = servicos.BuildServiceProvider();

        var excecao = Record.Exception(provedor.GetRequiredService<IStartupValidator>().Validate);

        Assert.True(
            excecao is OptionsValidationException
            || (excecao is AggregateException agregada && agregada.InnerExceptions.All(e => e is OptionsValidationException)),
            $"Exceção inesperada: {excecao}");
    }
}
