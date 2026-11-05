using System.Net;
using Api.Agente.Infrastructure.Configuracao;
using Api.Agente.Infrastructure.Resiliencia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Agente.Integration.Tests.Infraestrutura;

public class ResilienciaTests
{
    private sealed class HandlerContador(HttpStatusCode status) : HttpMessageHandler
    {
        public int Chamadas { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static (HttpClient Cliente, HandlerContador Handler) Criar(string nome, HttpStatusCode status)
    {
        var handler = new HandlerContador(status);
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Resiliencia:AtrasoBaseMilissegundos"] = "1" })
            .Build();

        var servicos = new ServiceCollection();
        servicos.AddSingleton<IConfiguration>(configuracao);
        servicos.AddLogging();
        servicos.AddOpcoesAgente();
        servicos.AddClientesResilientes();
        servicos.AddHttpClient(nome).ConfigurePrimaryHttpMessageHandler(() => handler);

        var cliente = servicos.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(nome);
        return (cliente, handler);
    }

    [Theory]
    [InlineData(ClientesHttp.OpenAi)]
    [InlineData(ClientesHttp.McpRag)]
    public async Task Erro_transitorio_tem_duas_retentativas(string nome)
    {
        var (cliente, handler) = Criar(nome, HttpStatusCode.ServiceUnavailable);

        var resposta = await cliente.GetAsync(new Uri("http://teste/"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Equal(3, handler.Chamadas);
    }

    [Fact]
    public async Task Erro_nao_transitorio_nao_tem_retentativa()
    {
        var (cliente, handler) = Criar(ClientesHttp.OpenAi, HttpStatusCode.BadRequest);

        await cliente.GetAsync(new Uri("http://teste/"), TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Chamadas);
    }
}
