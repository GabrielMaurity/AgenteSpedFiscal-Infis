using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Api.Agente.Application.TabelasReferencia;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Infrastructure.Persistencia.Postgres;
using Api.Agente.Integration.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Agente.Integration.Tests.TabelasReferencia;

/// <summary>Importação, versionamento e uso da tabela de NCM com bancos reais (FR-022, FR-063).</summary>
[Collection(ColecaoContainers.Nome)]
[Trait("Category", "Docker")]
public sealed class NcmEndpointsTests : IAsyncLifetime
{
    private static readonly Uri Rota = new("/api/v1/tabelas-referencia/ncm", UriKind.Relative);

    private readonly string _temporario = Path.Combine(Path.GetTempPath(), $"testes-ncm-{Guid.NewGuid():N}");
    private readonly FabricaApiAgente _fabrica;

    public NcmEndpointsTests(ContainersFixture containers)
    {
        var configuracao = FabricaApiAgente.ConfiguracaoPadrao();
        configuracao["MongoDb:ConnectionString"] = containers.MongoConnectionString;
        configuracao["MongoDb:Database"] = $"testes_{Guid.NewGuid():N}";
        configuracao["Postgres:ConnectionString"] = containers.PostgresConnectionString;
        configuracao["Arquivos:DiretorioTemporario"] = _temporario;
        _fabrica = new FabricaApiAgente { Configuracao = configuracao };
    }

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var escopo = _fabrica.Services.CreateAsyncScope();
        await escopo.ServiceProvider.GetRequiredService<AgenteDbContext>().Database.MigrateAsync(Cancelamento);
    }

    public async ValueTask DisposeAsync()
    {
        await _fabrica.DisposeAsync();
        if (Directory.Exists(_temporario))
        {
            Directory.Delete(_temporario, recursive: true);
        }
    }

    private HttpClient Cliente(string papel)
    {
        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FabricaApiAgente.CriarToken(papeis: papel));
        return cliente;
    }

    private static string VersaoUnica() => $"v-{Guid.NewGuid():N}";

    private Task<HttpResponseMessage> ImportarAsync(string versao, string inicio, string? fim, string conteudo = "32041700;Pigmentos\n")
    {
        var corpo = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(conteudo)), "arquivo", "ncm.csv" },
            { new StringContent("Fonte de teste"), "origem" },
            { new StringContent(versao), "versao" },
            { new StringContent(inicio), "vigenciaInicio" },
        };
        if (fim is not null)
        {
            corpo.Add(new StringContent(fim), "vigenciaFim");
        }

        return Cliente(Papeis.AdminBase).PostAsync(Rota, corpo, Cancelamento);
    }

    [Fact]
    public async Task Importacao_retorna_201_com_versao_hash_e_quantidade()
    {
        var versao = VersaoUnica();

        var resposta = await ImportarAsync(versao, "2041-01-01", "2041-12-31");

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
        Assert.Equal(versao, corpo.RootElement.GetProperty("versao").GetString());
        Assert.Equal(1, corpo.RootElement.GetProperty("quantidadeCodigos").GetInt32());
        Assert.Equal(64, corpo.RootElement.GetProperty("sha256").GetString()!.Length);
        Assert.Equal("usuario-teste", corpo.RootElement.GetProperty("importadoPor").GetString());
    }

    [Fact]
    public async Task Mesma_versao_retorna_409_sem_duplicar_os_codigos()
    {
        var versao = VersaoUnica();
        Assert.Equal(HttpStatusCode.Created, (await ImportarAsync(versao, "2042-01-01", "2042-12-31")).StatusCode);

        var repetida = await ImportarAsync(versao, "2042-01-01", "2042-12-31", "11111111;Outro\n22222222;Mais um\n");

        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        var lista = await _fabrica.Services.GetRequiredService<IRepositorioNcm>().ListarAsync(Cancelamento);
        Assert.Equal(1, Assert.Single(lista, t => t.Versao == versao).QuantidadeCodigos);
    }

    [Fact]
    public async Task Arquivo_sem_codigos_retorna_400()
    {
        var resposta = await ImportarAsync(VersaoUnica(), "2043-01-01", "2043-12-31", "nada aproveitável\n");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Listagem_e_permitida_ao_analista_e_mostra_a_versao_importada()
    {
        var versao = VersaoUnica();
        Assert.Equal(HttpStatusCode.Created, (await ImportarAsync(versao, "2044-01-01", "2044-12-31")).StatusCode);

        var resposta = await Cliente(Papeis.Analista).GetStringAsync(Rota, Cancelamento);

        Assert.Contains(versao, resposta, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versao_selecionada_e_a_vigente_no_periodo_do_arquivo()
    {
        var antiga = VersaoUnica();
        var nova = VersaoUnica();
        Assert.Equal(HttpStatusCode.Created, (await ImportarAsync(antiga, "2045-01-01", "2045-12-31", "11111111;Antigo\n")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ImportarAsync(nova, "2046-01-01", "2046-12-31", "22222222;Novo\n")).StatusCode);
        var repositorio = _fabrica.Services.GetRequiredService<IRepositorioNcm>();

        var de2045 = await repositorio.CarregarVigenteAsync(new DateOnly(2045, 6, 1), new DateOnly(2045, 6, 30), Cancelamento);
        var de2046 = await repositorio.CarregarVigenteAsync(new DateOnly(2046, 6, 1), new DateOnly(2046, 6, 30), Cancelamento);
        var de2047 = await repositorio.CarregarVigenteAsync(new DateOnly(2047, 6, 1), new DateOnly(2047, 6, 30), Cancelamento);

        Assert.Equal(antiga, de2045!.Versao);
        Assert.Equal(SituacaoNcm.Vigente, de2045.Consultar("11111111"));
        Assert.Equal(SituacaoNcm.Inexistente, de2045.Consultar("22222222"));
        Assert.Equal(nova, de2046!.Versao);
        Assert.Null(de2047);
    }

    [Fact]
    public async Task Validacao_usa_a_tabela_importada_e_aponta_o_ncm_no_envelope()
    {
        Assert.Equal(HttpStatusCode.Created, (await ImportarAsync(VersaoUnica(), "2026-01-01", "2026-12-31")).StatusCode);
        var cliente = Cliente(Papeis.Analista);
        using var corpo = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "efd", "ncm-inexistente.txt"), Cancelamento)),
                "arquivo",
                "ncm-inexistente.txt"
            },
        };

        var envio = await cliente.PostAsync(new Uri("/api/v1/validacoes", UriKind.Relative), corpo, Cancelamento);
        Assert.Equal(HttpStatusCode.Accepted, envio.StatusCode);

        JsonDocument? envelope = null;
        for (var tentativa = 0; tentativa < 100 && envelope is null; tentativa++)
        {
            var documento = JsonDocument.Parse(await cliente.GetStringAsync(envio.Headers.Location, Cancelamento));
            if (documento.RootElement.GetProperty("status").GetString() is "concluida" or "concluida_com_limitacoes" or "falha")
            {
                envelope = documento;
            }
            else
            {
                documento.Dispose();
                await Task.Delay(100, Cancelamento);
            }
        }

        Assert.NotNull(envelope);
        using (envelope)
        {
            var texto = envelope.RootElement.GetRawText();
            Assert.Contains(RegrasNegocio.NcmVigente, texto, StringComparison.Ordinal);
            Assert.Contains("99999999", texto, StringComparison.Ordinal);
        }
    }
}
