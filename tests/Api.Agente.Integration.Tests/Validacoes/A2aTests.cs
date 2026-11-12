using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using A2A;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;

namespace Api.Agente.Integration.Tests.Validacoes;

public sealed class A2aSemBancoTests(FabricaApiAgente fabrica) : IClassFixture<FabricaApiAgente>
{
    private static readonly Uri Cartao = new("/.well-known/agent-card.json", UriKind.Relative);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private HttpClient Cliente(params string[] papeis)
    {
        var cliente = fabrica.CreateClient();
        if (papeis.Length > 0)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FabricaApiAgente.CriarToken(papeis: papeis));
        }

        return cliente;
    }

    [Fact]
    public async Task Cartao_e_a2a_exigem_autenticacao()
    {
        var cliente = Cliente();

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync(Cartao, Cancelamento)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsync(new Uri("/a2a", UriKind.Relative), new StringContent("{}"), Cancelamento)).StatusCode);
    }

    [Fact]
    public async Task Papel_sem_permissao_recebe_403()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(Papeis.AdminBase).GetAsync(Cartao, Cancelamento)).StatusCode);
    }

    [Fact]
    public async Task Cartao_anuncia_as_duas_skills_implementadas()
    {
        var resposta = await Cliente(Papeis.AgenteExterno).GetAsync(Cartao, Cancelamento);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        using var cartao = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
        var skills = cartao.RootElement.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("id").GetString());
        Assert.Equal(["validar-efd-icms-ipi", "consultar-efd-icms-ipi"], skills);
        Assert.True(cartao.RootElement.TryGetProperty("securitySchemes", out _));
    }
}

/// <summary>A mesma entrada por REST e por A2A produz o mesmo resultado (P-20).</summary>
[Collection(ColecaoContainers.Nome)]
[Trait("Category", "Docker")]
public sealed class A2aEquivalenciaTests : IAsyncDisposable
{
    private readonly FabricaApiAgente _fabrica;

    public A2aEquivalenciaTests(ContainersFixture containers)
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao["MongoDb:ConnectionString"] = containers.MongoConnectionString;
        configuracao["MongoDb:Database"] = $"testes_{Guid.NewGuid():N}";
        configuracao["Postgres:ConnectionString"] = containers.PostgresConnectionString;
        configuracao["Arquivos:DiretorioTemporario"] = Path.Combine(Path.GetTempPath(), $"testes-a2a-{Guid.NewGuid():N}");
        _fabrica = new FabricaApiAgente { Configuracao = configuracao };
    }

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync() => await _fabrica.DisposeAsync();

    private HttpClient Cliente(string papel)
    {
        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FabricaApiAgente.CriarToken(papeis: papel));
        return cliente;
    }

    private static async Task<byte[]> FixtureAsync(string caso) =>
        await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "efd", $"{caso}.txt"), Cancelamento);

    private async Task<JsonElement> ValidarPorA2aAsync(string caso)
    {
        var http = Cliente(Papeis.AgenteExterno);
        var cliente = new A2AClient(new Uri(http.BaseAddress!, "/a2a"), http);

        var resposta = await cliente.SendMessageAsync(
            new SendMessageRequest
            {
                Message = new Message
                {
                    Role = Role.User,
                    MessageId = Guid.NewGuid().ToString("N"),
                    Parts = [Part.FromRaw(await FixtureAsync(caso), "text/plain", $"{caso}.txt")],
                },
            },
            Cancelamento);

        Assert.Equal(TaskState.Completed, resposta.Task!.Status.State);
        return resposta.Task.Artifacts!.Single().Parts.Single().Data!.Value;
    }

    private async Task<JsonElement> ValidarPorRestAsync(string caso)
    {
        var http = Cliente(Papeis.AgenteExterno);
        using var corpo = new MultipartFormDataContent { { new ByteArrayContent(await FixtureAsync(caso)), "arquivo", $"{caso}.txt" } };
        var envio = await http.PostAsync(new Uri("/api/v1/validacoes", UriKind.Relative), corpo, Cancelamento);
        for (var i = 0; i < 100; i++)
        {
            using var documento = JsonDocument.Parse(await http.GetStringAsync(envio.Headers.Location!, Cancelamento));
            if (documento.RootElement.GetProperty("status").GetString() is "concluida_com_limitacoes" or "falha")
            {
                return documento.RootElement.Clone();
            }

            await Task.Delay(100, Cancelamento);
        }

        throw new TimeoutException();
    }

    private static string[] Ids(JsonElement envelope) =>
        [.. envelope.GetProperty("achados").EnumerateArray().Select(a => a.GetProperty("id").GetString()!)];

    [Theory]
    [InlineData("valido-perfil-a")]
    [InlineData("sem-k990")]
    [InlineData("periodo-2027")]
    public async Task Mesma_entrada_gera_o_mesmo_resultado(string caso)
    {
        var a2a = await ValidarPorA2aAsync(caso);
        var rest = await ValidarPorRestAsync(caso);

        Assert.Equal(rest.GetProperty("status").GetString(), a2a.GetProperty("status").GetString());
        Assert.Equal(Ids(rest), Ids(a2a));
        Assert.Equal(rest.GetProperty("revisaoHumanaNecessaria").GetBoolean(), a2a.GetProperty("revisaoHumanaNecessaria").GetBoolean());
        Assert.Contains("PVA", a2a.GetProperty("avisoPva").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Arquivo_recusado_rejeita_a_tarefa()
    {
        var http = Cliente(Papeis.AgenteExterno);
        var cliente = new A2AClient(new Uri(http.BaseAddress!, "/a2a"), http);

        var resposta = await cliente.SendMessageAsync(
            new SendMessageRequest
            {
                Message = new Message
                {
                    Role = Role.User,
                    MessageId = Guid.NewGuid().ToString("N"),
                    Parts = [Part.FromRaw([65, 66], "text/plain", "efd.pdf")],
                },
            },
            Cancelamento);

        Assert.Equal(TaskState.Rejected, resposta.Task!.Status.State);
    }
}
