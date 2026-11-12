using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Api.Agente.Application.Exportacao;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;
using Json.Schema;

namespace Api.Agente.Integration.Tests.Validacoes;

/// <summary>Fluxo completo da história 1 com MongoDB real (quickstart §4).</summary>
[Collection(ColecaoContainers.Nome)]
[Trait("Category", "Docker")]
public sealed class ValidacoesEndpointsTests : IAsyncDisposable
{
    internal static readonly Lazy<JsonSchema> SchemaEnvelope = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contratos", "envelope-validacao.schema.json"))));

    private readonly string _temporario = Path.Combine(Path.GetTempPath(), $"testes-efd-{Guid.NewGuid():N}");
    private readonly FabricaApiAgente _fabrica;

    public ValidacoesEndpointsTests(ContainersFixture containers)
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao["MongoDb:ConnectionString"] = containers.MongoConnectionString;
        configuracao["MongoDb:Database"] = $"testes_{Guid.NewGuid():N}";
        configuracao["Postgres:ConnectionString"] = containers.PostgresConnectionString;
        configuracao["Arquivos:DiretorioTemporario"] = _temporario;
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

    private HttpClient Cliente(string organizacao = "org-a")
    {
        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", FabricaApiAgente.CriarToken(orgId: organizacao, papeis: Papeis.Analista));
        return cliente;
    }

    private static string CaminhoFixture(string caso) => Path.Combine(AppContext.BaseDirectory, "fixtures", "efd", $"{caso}.txt");

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string caso)
    {
        using var corpo = new MultipartFormDataContent
        {
            { new ByteArrayContent(await File.ReadAllBytesAsync(CaminhoFixture(caso), Cancelamento)), "arquivo", $"{caso}.txt" },
        };
        return await cliente.PostAsync(new Uri("/api/v1/validacoes", UriKind.Relative), corpo, Cancelamento);
    }

    private static async Task<JsonDocument> AguardarFimAsync(HttpClient cliente, Uri local)
    {
        for (var tentativa = 0; tentativa < 100; tentativa++)
        {
            var resposta = await cliente.GetAsync(local, Cancelamento);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
            var status = documento.RootElement.GetProperty("status").GetString();
            if (status is "concluida" or "concluida_com_limitacoes" or "falha")
            {
                return documento;
            }

            documento.Dispose();
            await Task.Delay(100, Cancelamento);
        }

        throw new TimeoutException("A execução não terminou no tempo esperado.");
    }

    private static async Task<(Uri Local, JsonDocument Envelope)> ValidarAsync(HttpClient cliente, string caso)
    {
        var envio = await EnviarAsync(cliente, caso);
        Assert.Equal(HttpStatusCode.Accepted, envio.StatusCode);
        var local = envio.Headers.Location!;
        return (local, await AguardarFimAsync(cliente, local));
    }

    private static void AssertValidoNoContrato(JsonDocument envelope)
    {
        var resultado = SchemaEnvelope.Value.Evaluate(envelope.RootElement);
        Assert.True(resultado.IsValid, envelope.RootElement.GetRawText());
    }

    [Fact]
    public async Task Envio_retorna_202_com_local_e_identificador()
    {
        var resposta = await EnviarAsync(Cliente(), "valido-perfil-a");

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        Assert.NotNull(resposta.Headers.Location);
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
        Assert.True(Guid.TryParse(corpo.RootElement.GetProperty("execucaoId").GetString(), out _));
        Assert.Equal("recebida", corpo.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Arquivo_valido_segue_o_contrato_sem_declarar_conformidade()
    {
        var (_, envelope) = await ValidarAsync(Cliente(), "valido-perfil-a");
        using (envelope)
        {
            var raiz = envelope.RootElement;
            AssertValidoNoContrato(envelope);
            Assert.Equal("concluida_com_limitacoes", raiz.GetProperty("status").GetString());
            Assert.Equal(0, raiz.GetProperty("achados").GetArrayLength());
            Assert.Contains("PVA permanece obrigatória", raiz.GetProperty("avisoPva").GetString(), StringComparison.Ordinal);
            Assert.Equal("020", raiz.GetProperty("identificacao").GetProperty("codVer").GetString());
            Assert.Equal("2026-01-31", raiz.GetProperty("identificacao").GetProperty("periodoFim").GetString());
            Assert.Equal(9, raiz.GetProperty("etapas").GetArrayLength());
            Assert.DoesNotContain("conforme", raiz.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Arquivo_sem_k990_traz_achado_com_regra_e_fonte()
    {
        var (_, envelope) = await ValidarAsync(Cliente(), "sem-k990");
        using (envelope)
        {
            AssertValidoNoContrato(envelope);
            var achado = envelope.RootElement.GetProperty("achados").EnumerateArray().Single();
            Assert.Equal("erro_estrutural", achado.GetProperty("categoria").GetString());
            Assert.Equal("impeditiva", achado.GetProperty("severidade").GetString());
            Assert.Equal("K990", achado.GetProperty("registro").GetString());
            Assert.Equal("EFD-ESTR-011", achado.GetProperty("regraId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(achado.GetProperty("fonte").GetProperty("localizacao").GetString()));
        }
    }

    [Theory]
    [InlineData("periodo-2025")]
    [InlineData("periodo-2027")]
    public async Task Periodo_fora_do_leiaute_020_termina_em_falha_na_etapa_3(string caso)
    {
        var (_, envelope) = await ValidarAsync(Cliente(), caso);
        using (envelope)
        {
            AssertValidoNoContrato(envelope);
            Assert.Equal("falha", envelope.RootElement.GetProperty("status").GetString());
            var etapa3 = envelope.RootElement.GetProperty("etapas")[2];
            Assert.Equal("falha", etapa3.GetProperty("status").GetString());
            Assert.Contains("Período não suportado", etapa3.GetProperty("motivo").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Arquivo_temporario_nao_existe_depois_do_processamento()
    {
        var (_, envelope) = await ValidarAsync(Cliente(), "valido-perfil-a");
        envelope.Dispose();

        Assert.Empty(Directory.Exists(_temporario) ? Directory.GetFiles(_temporario) : []);
    }

    [Fact]
    public async Task Outra_organizacao_nao_acessa_a_execucao()
    {
        var (local, envelope) = await ValidarAsync(Cliente("org-a"), "valido-perfil-a");
        envelope.Dispose();
        var outra = Cliente("org-b");

        Assert.Equal(HttpStatusCode.NotFound, (await outra.GetAsync(local, Cancelamento)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outra.DeleteAsync(local, Cancelamento)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outra.GetAsync(new Uri($"{local}/exportacao.csv", UriKind.Relative), Cancelamento)).StatusCode);
    }

    [Fact]
    public async Task Listagem_traz_so_as_execucoes_da_organizacao()
    {
        var (_, envelope) = await ValidarAsync(Cliente("org-lista"), "valido-perfil-a");
        envelope.Dispose();

        using var propria = JsonDocument.Parse(await Cliente("org-lista").GetStringAsync(new Uri("/api/v1/validacoes", UriKind.Relative), Cancelamento));
        using var outra = JsonDocument.Parse(await Cliente("org-vazia").GetStringAsync(new Uri("/api/v1/validacoes", UriKind.Relative), Cancelamento));

        Assert.Equal(1, propria.RootElement.GetArrayLength());
        Assert.Equal(0, outra.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Exclusao_antecipada_remove_a_execucao()
    {
        var cliente = Cliente();
        var (local, envelope) = await ValidarAsync(cliente, "valido-perfil-a");
        envelope.Dispose();

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.DeleteAsync(local, Cancelamento)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync(local, Cancelamento)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync(new Uri($"{local}/exportacao.csv", UriKind.Relative), Cancelamento)).StatusCode);
    }

    [Fact]
    public async Task Csv_tem_os_mesmos_achados_do_envelope()
    {
        var cliente = Cliente();
        var (local, envelope) = await ValidarAsync(cliente, "ind-mov-incoerente");
        using (envelope)
        {
            var resposta = await cliente.GetAsync(new Uri($"{local}/exportacao.csv", UriKind.Relative), Cancelamento);

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.Equal("text/csv", resposta.Content.Headers.ContentType?.MediaType);
            Assert.Contains("validacao-", resposta.Content.Headers.ContentDisposition?.FileName, StringComparison.Ordinal);
            var bytes = await resposta.Content.ReadAsByteArrayAsync(Cancelamento);
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

            var linhas = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
            var cabecalho = Array.IndexOf(linhas, ExportadorCsv.Cabecalho);
            var idsCsv = linhas[(cabecalho + 1)..].Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split(';')[0]).ToList();
            var idsEnvelope = envelope.RootElement.GetProperty("achados").EnumerateArray().Select(a => a.GetProperty("id").GetString()!).ToList();

            Assert.True(cabecalho > 0);
            Assert.NotEmpty(idsCsv);
            Assert.Equal(idsEnvelope, idsCsv);
        }
    }
}
