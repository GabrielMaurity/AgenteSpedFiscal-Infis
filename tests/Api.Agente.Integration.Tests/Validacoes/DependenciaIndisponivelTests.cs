using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;

namespace Api.Agente.Integration.Tests.Validacoes;

/// <summary>Com a Api.Rag parada, os achados determinísticos saem completos e a limitação é declarada (SC-004, FR-084).</summary>
[Collection(ColecaoContainers.Nome)]
[Trait("Category", "Docker")]
public sealed class DependenciaIndisponivelTests : IAsyncDisposable
{
    private readonly string _temporario = Path.Combine(Path.GetTempPath(), $"testes-dependencia-{Guid.NewGuid():N}");
    private readonly FabricaApiAgente _fabrica;

    public DependenciaIndisponivelTests(ContainersFixture containers)
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao["MongoDb:ConnectionString"] = containers.MongoConnectionString;
        configuracao["MongoDb:Database"] = $"testes_{Guid.NewGuid():N}";
        configuracao["Postgres:ConnectionString"] = containers.PostgresConnectionString;
        configuracao["Arquivos:DiretorioTemporario"] = _temporario;

        // Token configurado e endereço sem servidor: o caminho real do cliente MCP, com a resiliência.
        configuracao["McpRag:TokenServico"] = FabricaApiAgente.CriarToken(orgId: null, audiencia: "api-rag");
        _fabrica = new FabricaApiAgente { Configuracao = configuracao };
    }

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _fabrica.DisposeAsync();
        if (Directory.Exists(_temporario))
        {
            Directory.Delete(_temporario, recursive: true);
        }
    }

    [Theory]
    [InlineData("valido-perfil-a")]
    [InlineData("unidade-sem-0190")]
    public async Task Api_rag_parada_termina_com_limitacoes_e_consulta_listada_como_falha_tecnica(string caso)
    {
        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FabricaApiAgente.CriarToken(papeis: Papeis.Analista));
        using var corpo = new MultipartFormDataContent
        {
            { new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "efd", $"{caso}.txt"), Cancelamento)), "arquivo", $"{caso}.txt" },
        };

        var envio = await cliente.PostAsync(new Uri("/api/v1/validacoes", UriKind.Relative), corpo, Cancelamento);
        Assert.Equal(HttpStatusCode.Accepted, envio.StatusCode);
        using var envelope = await AguardarFimAsync(cliente, envio.Headers.Location!);

        var raiz = envelope.RootElement;
        Assert.True(ValidacoesEndpointsTests.SchemaEnvelope.Value.Evaluate(raiz).IsValid);
        Assert.Equal("concluida_com_limitacoes", raiz.GetProperty("status").GetString());
        var consulta = raiz.GetProperty("validacoesNaoRealizadas").EnumerateArray().Single(v => v.GetProperty("referencia").GetString() == "etapa-5");
        Assert.Equal("falha_tecnica", consulta.GetProperty("categoria").GetString());
        Assert.Equal("falha", raiz.GetProperty("etapas")[4].GetProperty("status").GetString());
        Assert.False(raiz.TryGetProperty("analiseIa", out _));
        Assert.True(raiz.GetProperty("revisaoHumanaNecessaria").GetBoolean());

        // Os achados determinísticos são os do gabarito, sem perda por causa da dependência.
        using var gabarito = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "efd", $"{caso}.gabarito.json"), Cancelamento));
        Assert.Equal(
            gabarito.RootElement.GetProperty("achados").EnumerateArray().Select(a => a.GetProperty("regraId").GetString()).Order(),
            raiz.GetProperty("achados").EnumerateArray().Select(a => a.GetProperty("regraId").GetString()).Order());
    }

    private static async Task<JsonDocument> AguardarFimAsync(HttpClient cliente, Uri local)
    {
        for (var tentativa = 0; tentativa < 300; tentativa++)
        {
            var documento = JsonDocument.Parse(await cliente.GetStringAsync(local, Cancelamento));
            if (documento.RootElement.GetProperty("status").GetString() is "concluida" or "concluida_com_limitacoes" or "falha")
            {
                return documento;
            }

            documento.Dispose();
            await Task.Delay(100, Cancelamento);
        }

        throw new TimeoutException("A execução não terminou no tempo esperado.");
    }
}
