using System.Net;
using System.Text;
using System.Text.Json;
using AgenteSpedFiscal.Contracts.Mcp;
using Api.Rag.Tests.Infra;
using Api.Rag.Tests.Persistencia;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Api.Rag.Tests.Mcp;

/// <summary>Cliente MCP real sobre a API em memória (contrato mcp-api-rag.md).</summary>
internal static class ClienteMcp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<McpClient> ConectarAsync(FabricaApiRag fabrica, string token)
    {
        var http = fabrica.Cliente(token);
        var transporte = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            NullLoggerFactory.Instance,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transporte, cancellationToken: TestContext.Current.CancellationToken);
    }

    public static Task<CallToolResult> ChamarAsync(McpClient cliente, string ferramenta, Dictionary<string, object?> argumentos) =>
        cliente.CallToolAsync(ferramenta, argumentos, cancellationToken: TestContext.Current.CancellationToken).AsTask();

    public static string Texto(CallToolResult resultado) => string.Concat(resultado.Content.OfType<TextContentBlock>().Select(c => c.Text));

    public static T Ler<T>(CallToolResult resultado)
    {
        Assert.NotEqual(true, resultado.IsError);
        return resultado.StructuredContent is { } estruturado
            ? estruturado.Deserialize<T>(Json)!
            : JsonSerializer.Deserialize<T>(Texto(resultado), Json)!;
    }
}

/// <summary>Autorização e validação de argumentos, que não chegam ao banco.</summary>
public sealed class FerramentasMcpSemBancoTests(FabricaApiRag fabrica) : IClassFixture<FabricaApiRag>
{
    private static readonly string TokenServico = FabricaApiRag.CriarToken(orgId: null);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Chamada_sem_token_de_servico_e_negada()
    {
        using var corpo = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");

        var resposta = await fabrica.Cliente(null).PostAsync(new Uri("/mcp", UriKind.Relative), corpo, Cancelamento);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Servidor_anuncia_as_tres_ferramentas_somente_leitura()
    {
        await using var cliente = await ClienteMcp.ConectarAsync(fabrica, TokenServico);

        var ferramentas = await cliente.ListToolsAsync(cancellationToken: Cancelamento);

        Assert.Equal(
            [ContratoMcp.BuscarTrechos, ContratoMcp.ListarDocumentos, ContratoMcp.ObterTrecho],
            ferramentas.Select(f => f.Name).Order());
        Assert.All(ferramentas, f => Assert.True(f.ProtocolTool.Annotations?.ReadOnlyHint));
    }

    [Theory]
    [InlineData("3.2.4", "2026-01")]
    [InlineData(null, null)]
    public async Task Buscar_trechos_exige_exatamente_um_entre_versao_e_periodo(string? versao, string? periodo)
    {
        await using var cliente = await ClienteMcp.ConectarAsync(fabrica, TokenServico);

        var resultado = await ClienteMcp.ChamarAsync(cliente, ContratoMcp.BuscarTrechos, new()
        {
            ["consulta"] = "C190", ["tipoDocumento"] = "guia_pratico_efd_icms_ipi", ["correlacaoId"] = "c",
            ["versao"] = versao, ["periodo"] = periodo,
        });

        Assert.True(resultado.IsError);
        Assert.Contains("exatamente um entre versao e periodo", ClienteMcp.Texto(resultado), StringComparison.Ordinal);
    }
}

/// <summary>Base com duas versões do Guia e um material interno, ingerida uma vez para a classe.</summary>
public sealed class BaseIngeridaFixture : IAsyncLifetime
{
    private readonly PostgresFixture _banco = new();

    public FabricaApiRag Fabrica { get; private set; } = null!;
    public Guid Guia2025 { get; private set; }
    public Guid Guia2026 { get; private set; }
    public Guid Interno { get; private set; }
    public Guid Desativado { get; private set; }
    public string MarcadorInterno { get; } = $"rotinainterna{Guid.NewGuid():N}";

    public async ValueTask InitializeAsync()
    {
        await _banco.InitializeAsync();
        var configuracao = FabricaApiRag.ConfiguracaoPadrao();
        configuracao["Postgres:ConnectionString"] = _banco.ConnectionString;
        Fabrica = new FabricaApiRag { Configuracao = configuracao, Embeddings = new GeradorEmbeddingsFalso() };
        var admin = Fabrica.Cliente(FabricaApiRag.CriarToken(papeis: "admin_base"));

        Guia2026 = await Administracao.IngerirEAguardarAsync(admin, PdfAmostra.Gerar(PdfAmostra.PaginasDoGuia()), "guia-3.2.4.pdf",
            versao: "3.2.4", vigenciaInicio: "2026-01-01");
        Guia2025 = await Administracao.IngerirEAguardarAsync(
            admin,
            PdfAmostra.Gerar([["REGISTRO C190: REGISTRO ANALÍTICO DO DOCUMENTO NA VERSÃO ANTERIOR", "01 REG Texto fixo contendo C190", "Campo 05 (VL_OPR) - versão anterior"]]),
            "guia-3.2.3.pdf", versao: "3.2.3", vigenciaInicio: "2025-01-01", vigenciaFim: "2025-12-31");
        Interno = await Administracao.IngerirEAguardarAsync(admin, Encoding.UTF8.GetBytes($"# Rotina\n\nA {MarcadorInterno} do C190 é conferida pelo analista.\n"),
            "rotina.md", "interno", "material_interno", "1");
        Desativado = await Administracao.IngerirEAguardarAsync(admin, Encoding.UTF8.GetBytes("# Antigo\n\nTexto desativado sobre o C190.\n"),
            "antigo.md", "interno", "material_interno", "1");
        var resposta = await admin.PostAsync(new Uri($"/api/v1/documentos/{Desativado}/desativar", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await Fabrica.DisposeAsync();
        await _banco.DisposeAsync();
    }
}

[Trait("Category", "Docker")]
public sealed class FerramentasMcpTests(BaseIngeridaFixture baseIngerida) : IClassFixture<BaseIngeridaFixture>
{
    private static readonly string TokenServico = FabricaApiRag.CriarToken(orgId: null);
    private static readonly string[] SoOficial = ["oficial"];
    private static readonly string[] SoInterno = ["interno"];

    private Task<McpClient> ConectarAsync() => ClienteMcp.ConectarAsync(baseIngerida.Fabrica, TokenServico);

    private async Task<BuscarTrechosResponse> BuscarAsync(Dictionary<string, object?> argumentos)
    {
        await using var cliente = await ConectarAsync();
        argumentos.TryAdd("tipoDocumento", "guia_pratico_efd_icms_ipi");
        argumentos.TryAdd("correlacaoId", "correlacao-teste");
        return ClienteMcp.Ler<BuscarTrechosResponse>(await ClienteMcp.ChamarAsync(cliente, ContratoMcp.BuscarTrechos, argumentos));
    }

    [Fact]
    public async Task Filtra_pela_versao_informada()
    {
        var resposta = await BuscarAsync(new() { ["consulta"] = "registro analítico C190", ["versao"] = "3.2.3", ["classificacoes"] = SoOficial });

        Assert.NotEmpty(resposta.Trechos);
        Assert.All(resposta.Trechos, t => Assert.Equal(baseIngerida.Guia2025, t.Documento.Id));
    }

    [Fact]
    public async Task Periodo_seleciona_a_versao_vigente()
    {
        var em2026 = await BuscarAsync(new() { ["consulta"] = "C190", ["periodo"] = "2026-03", ["classificacoes"] = SoOficial });
        var em2025 = await BuscarAsync(new() { ["consulta"] = "C190", ["periodo"] = "2025-03", ["classificacoes"] = SoOficial });

        Assert.All(em2026.Trechos, t => Assert.Equal("3.2.4", t.Documento.Versao));
        Assert.All(em2025.Trechos, t => Assert.Equal("3.2.3", t.Documento.Versao));
        Assert.NotEmpty(em2026.Trechos);
        Assert.NotEmpty(em2025.Trechos);
    }

    [Fact]
    public async Task Codigo_exato_traz_o_registro_no_topo_com_classificacao_e_documento()
    {
        var resposta = await BuscarAsync(new() { ["consulta"] = "C190", ["versao"] = "3.2.4", ["classificacoes"] = SoOficial });

        var primeiro = resposta.Trechos[0];
        Assert.Equal("C190", primeiro.Localizacao.Registro);
        Assert.Equal(Classificacao.Oficial, primeiro.Classificacao);
        Assert.Equal(baseIngerida.Guia2026, primeiro.Documento.Id);
        Assert.Equal("guia_pratico_efd_icms_ipi", primeiro.Documento.TipoDocumento);
        Assert.True(primeiro.Localizacao.PaginaInicio > 0);
        Assert.False(resposta.SemResultados);
        Assert.Equal(ContratoMcp.Versao, resposta.VersaoContrato);
    }

    [Fact]
    public async Task Filtra_por_classificacao()
    {
        var internos = await BuscarAsync(new() { ["consulta"] = baseIngerida.MarcadorInterno, ["periodo"] = "2026-01", ["classificacoes"] = SoInterno });
        var oficiais = await BuscarAsync(new() { ["consulta"] = baseIngerida.MarcadorInterno, ["periodo"] = "2026-01", ["classificacoes"] = SoOficial });

        Assert.Contains(internos.Trechos, t => t.Documento.Id == baseIngerida.Interno);
        Assert.All(internos.Trechos, t => Assert.Equal(Classificacao.Interno, t.Classificacao));
        Assert.All(oficiais.Trechos, t => Assert.Equal(Classificacao.Oficial, t.Classificacao));
    }

    [Fact]
    public async Task Documento_desativado_nao_aparece_na_busca()
    {
        var resposta = await BuscarAsync(new() { ["consulta"] = "Texto desativado sobre o C190", ["periodo"] = "2026-01", ["classificacoes"] = SoInterno });

        Assert.DoesNotContain(resposta.Trechos, t => t.Documento.Id == baseIngerida.Desativado);
    }

    [Fact]
    public async Task Sem_correspondencia_devolve_sem_resultados()
    {
        var resposta = await BuscarAsync(new()
        {
            ["consulta"] = "C190", ["tipoDocumento"] = "ato_cotepe", ["versao"] = "1", ["classificacoes"] = SoOficial,
        });

        Assert.True(resposta.SemResultados);
        Assert.Empty(resposta.Trechos);
    }

    [Fact]
    public async Task Obter_trecho_devolve_o_trecho_e_nao_encontra_o_de_documento_inativo()
    {
        var busca = await BuscarAsync(new() { ["consulta"] = "C190", ["versao"] = "3.2.4", ["classificacoes"] = SoOficial });
        await using var cliente = await ConectarAsync();

        var obtido = ClienteMcp.Ler<ObterTrechoResponse>(await ClienteMcp.ChamarAsync(cliente, ContratoMcp.ObterTrecho, new()
        {
            ["trechoId"] = busca.Trechos[0].TrechoId, ["correlacaoId"] = "c",
        }));
        var inexistente = await ClienteMcp.ChamarAsync(cliente, ContratoMcp.ObterTrecho, new()
        {
            ["trechoId"] = Guid.NewGuid(), ["correlacaoId"] = "c",
        });

        Assert.Equal(busca.Trechos[0].TrechoId, obtido.Trecho.TrechoId);
        Assert.True(inexistente.IsError);
        Assert.Contains(ContratoMcp.ErroNaoEncontrado, ClienteMcp.Texto(inexistente), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listar_documentos_traz_so_os_ativos_vigentes()
    {
        await using var cliente = await ConectarAsync();

        var lista = ClienteMcp.Ler<ListarDocumentosResponse>(await ClienteMcp.ChamarAsync(cliente, ContratoMcp.ListarDocumentos, new()
        {
            ["correlacaoId"] = "c", ["periodo"] = "2026-06",
        }));

        var ids = lista.Documentos.Select(d => d.Id).ToList();
        Assert.Contains(baseIngerida.Guia2026, ids);
        Assert.Contains(baseIngerida.Interno, ids);
        Assert.DoesNotContain(baseIngerida.Guia2025, ids);
        Assert.DoesNotContain(baseIngerida.Desativado, ids);
    }
}
