using System.Net;
using System.Text.Json;
using Api.Rag.Application.Consumo;
using Api.Rag.Endpoints.Infraestrutura;
using Api.Rag.Endpoints.Seguranca;
using Api.Rag.Infrastructure.Configuracao;
using Api.Rag.Infrastructure.Consumo;
using Api.Rag.Infrastructure.Observabilidade;
using Api.Rag.Tests.Infra;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Rag.Tests.Infraestrutura;

public sealed class HospedagemTests(FabricaApiRag fabrica) : IClassFixture<FabricaApiRag>
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private async Task<bool> Autoriza(System.Security.Claims.ClaimsPrincipal usuario, string politica)
    {
        _ = fabrica.Server;
        return (await fabrica.Services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(usuario, politica)).Succeeded;
    }

    [Fact]
    public async Task Health_live_responde_sem_autenticacao()
    {
        var resposta = await fabrica.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative), Cancelamento);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Health_ready_informa_indisponibilidade_sem_detalhes()
    {
        var resposta = await fabrica.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancelamento);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Equal("Unhealthy", await resposta.Content.ReadAsStringAsync(Cancelamento));
    }

    [Fact]
    public async Task Erros_seguem_rfc_9457_com_correlacao()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add(CorrelacaoMiddleware.Cabecalho, "erro-1");

        var resposta = await cliente.GetAsync(new Uri("/nao-existe", UriKind.Relative), Cancelamento);

        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("erro-1", resposta.Headers.GetValues(CorrelacaoMiddleware.Cabecalho).Single());
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
        Assert.Equal("erro-1", corpo.RootElement.GetProperty("correlacaoId").GetString());
    }

    [Theory]
    [InlineData("OpenAi:ApiKey")]
    [InlineData("Postgres:ConnectionString")]
    public void Configuracao_ausente_impede_o_inicio(string chave)
    {
        var configuracao = FabricaApiRag.ConfiguracaoPadrao();
        configuracao.Remove(chave);
        AssertInicioRecusado(configuracao);
    }

    [Fact]
    public void Dimensao_diferente_da_coluna_impede_o_inicio()
    {
        var configuracao = FabricaApiRag.ConfiguracaoPadrao();
        configuracao["OpenAi:Dimensao"] = "3072";
        AssertInicioRecusado(configuracao);
    }

    // Validação direta: o que ValidateOnStart executa no início, sem a corrida de subir um host que falha.
    private static void AssertInicioRecusado(Dictionary<string, string?> configuracao)
    {
        var servicos = new ServiceCollection();
        servicos.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuracao).Build());
        servicos.AddOpcoesRag();
        using var provedor = servicos.BuildServiceProvider();

        var excecao = Record.Exception(provedor.GetRequiredService<IStartupValidator>().Validate);

        Assert.True(
            excecao is OptionsValidationException
            || (excecao is AggregateException agregada && agregada.InnerExceptions.All(e => e is OptionsValidationException)),
            $"Exceção inesperada: {excecao}");
    }

    [Fact]
    public async Task Rest_de_administracao_exige_o_papel_e_a_organizacao()
    {
        Assert.True(await Autoriza(FabricaApiRag.Usuario(null, "org", "admin_base"), Politicas.AdminBase));
        Assert.False(await Autoriza(FabricaApiRag.Usuario(null, "org", "analista"), Politicas.AdminBase));
        Assert.False(await Autoriza(FabricaApiRag.Usuario(null, null, "admin_base"), Politicas.AdminBase));
    }

    [Fact]
    public async Task Mcp_exige_audiencia_de_servico()
    {
        Assert.True(await Autoriza(FabricaApiRag.Usuario("api-rag", null), Politicas.ServicoMcp));
        Assert.False(await Autoriza(FabricaApiRag.Usuario("api-agente", "org", "admin_base"), Politicas.ServicoMcp));
    }

    [Theory]
    [InlineData("CNPJ 12.345.678/0001-95 e CPF 123.456.789-09", "CNPJ *** e CPF ***")]
    [InlineData("Documento 019a5c2e-7b1f-7c3a-9d4e-1f2a3b4c5d6e ativo", "Documento 019a5c2e-7b1f-7c3a-9d4e-1f2a3b4c5d6e ativo")]
    public void Mascara_documentos_e_preserva_identificadores(string entrada, string esperado) =>
        Assert.Equal(esperado, MascaradorDadosSensiveis.Mascarar(entrada));

    [Fact]
    public void Consumo_desconhecido_fica_nulo_e_nunca_zero()
    {
        var consumo = RegistradorConsumoEmbeddings.Montar(
            new RegistroConsumoEmbeddings(null, "c", "openai", "m", 1, null, DateTimeOffset.UtcNow, 10, "falha"));

        Assert.Null(consumo.TokensEntrada);
        Assert.Equal("desconhecido", consumo.OrigemTokens);
        Assert.Null(consumo.CustoEstimado);
    }

    [Fact]
    public void Consumo_informado_preserva_tokens_e_tentativa()
    {
        var consumo = RegistradorConsumoEmbeddings.Montar(
            new RegistroConsumoEmbeddings(null, "c", "openai", "m", 2, 0, DateTimeOffset.UtcNow, 10, "sucesso"));

        Assert.Equal(0, consumo.TokensEntrada);
        Assert.Equal("informado", consumo.OrigemTokens);
        Assert.Equal(2, consumo.Tentativa);
    }
}
