using System.Net;
using System.Text;
using System.Text.Json;
using AgenteSpedFiscal.Contracts.Mcp;
using Api.Rag.Application.Busca;
using Api.Rag.Infrastructure.Persistencia;
using Api.Rag.Tests.Infra;
using Api.Rag.Tests.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Rag.Tests.Ingestao;

/// <summary>Ingestão pela API, com PostgreSQL real e embeddings falsos (FR-060, FR-061).</summary>
[Trait("Category", "Docker")]
public sealed class IngestaoTests : IClassFixture<PostgresFixture>, IAsyncDisposable
{
    private readonly PostgresFixture _banco;
    private readonly GeradorEmbeddingsFalso _embeddings = new();
    private readonly FabricaApiRag _fabrica;

    public IngestaoTests(PostgresFixture banco)
    {
        _banco = banco;
        var configuracao = FabricaApiRag.ConfiguracaoPadrao();
        configuracao["Postgres:ConnectionString"] = banco.ConnectionString;
        _fabrica = new FabricaApiRag { Configuracao = configuracao, Embeddings = _embeddings };
    }

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _fabrica.DisposeAsync();

    private HttpClient Admin() => _fabrica.Cliente(FabricaApiRag.CriarToken(papeis: "admin_base"));

    /// <summary>Cada teste usa um conteúdo único: o mesmo SHA-256 seria recusado.</summary>
    private static byte[] GuiaUnico() =>
        PdfAmostra.Gerar([.. PdfAmostra.PaginasDoGuia().Select((p, i) => i == 0 ? p.Prepend($"Exemplar {Guid.NewGuid():N}") : p)]);

    private static byte[] Markdown(string texto) => Encoding.UTF8.GetBytes(texto);

    [Fact]
    public async Task Guia_em_pdf_fica_ativo_com_trechos_e_modelo_gravado()
    {
        var cliente = Admin();
        var id = await Administracao.IngerirEAguardarAsync(cliente, GuiaUnico(), "guia.pdf");

        using var detalhe = JsonDocument.Parse(await cliente.GetStringAsync(new Uri($"/api/v1/documentos/{id}", UriKind.Relative), Cancelamento));
        var raiz = detalhe.RootElement;
        Assert.True(raiz.GetProperty("quantidadeTrechos").GetInt32() > 3);
        Assert.Equal("falso-palavras", raiz.GetProperty("modeloEmbedding").GetString());
        Assert.Equal(1536, raiz.GetProperty("dimensao").GetInt32());
        Assert.Equal("oficial", raiz.GetProperty("classificacao").GetString());

        await using var banco = _banco.Criar();
        var c190 = await banco.Trechos.Where(t => t.DocumentoId == id && t.Texto.StartsWith("REGISTRO C190")).SingleAsync(Cancelamento);
        Assert.Contains("12 COD_OBS", c190.Texto, StringComparison.Ordinal);
        Assert.Contains("\"registro\": \"C190\"", c190.Localizacao, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mesmo_arquivo_retorna_409_com_o_documento_existente()
    {
        var cliente = Admin();
        var arquivo = GuiaUnico();
        var id = await Administracao.IngerirEAguardarAsync(cliente, arquivo, "guia.pdf");

        var repetido = await Administracao.IngerirAsync(cliente, arquivo, "outro-nome.pdf", versao: "9.9.9");

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        using var corpo = JsonDocument.Parse(await repetido.Content.ReadAsStringAsync(Cancelamento));
        Assert.Equal(id, corpo.RootElement.GetProperty("documentoId").GetGuid());
    }

    [Fact]
    public async Task Reprocessar_mantem_os_mesmos_trechos_sem_duplicar()
    {
        var cliente = Admin();
        var id = await Administracao.IngerirEAguardarAsync(cliente, GuiaUnico(), "guia.pdf");
        await using var banco = _banco.Criar();
        var antes = await banco.Trechos.Where(t => t.DocumentoId == id).Select(t => t.Id).OrderBy(t => t).ToListAsync(Cancelamento);
        var chamadas = _embeddings.Chamadas;

        var resposta = await cliente.PostAsync(new Uri($"/api/v1/documentos/{id}/reprocessar", UriKind.Relative), null, Cancelamento);
        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        Assert.Equal("ativo", await Administracao.AguardarAsync(cliente, id));

        var depois = await banco.Trechos.Where(t => t.DocumentoId == id).Select(t => t.Id).OrderBy(t => t).ToListAsync(Cancelamento);
        Assert.Equal(antes, depois);
        // Mesmo modelo e mesmo texto: nenhum embedding novo foi pedido.
        Assert.Equal(chamadas, _embeddings.Chamadas);
    }

    [Theory]
    [InlineData(null, "guia_pratico_efd_icms_ipi", "3.2.4")]
    [InlineData("oficial", "guia_pratico_efd_icms_ipi", null)]
    [InlineData("interno", "guia_pratico_efd_icms_ipi", "1")]
    [InlineData("oficial", "material_interno", "1")]
    [InlineData("publico", "guia_pratico_efd_icms_ipi", "1")]
    public async Task Metadados_ausentes_ou_incompativeis_retornam_400(string? classificacao, string tipo, string? versao)
    {
        var resposta = await Administracao.IngerirAsync(Admin(), GuiaUnico(), "guia.pdf", classificacao, tipo, versao);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Formato_nao_aceito_retorna_400()
    {
        var resposta = await Administracao.IngerirAsync(Admin(), [1, 2, 3], "planilha.xlsx");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Material_interno_em_markdown_fica_ativo()
    {
        var cliente = Admin();
        var texto = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "base", "faq-interno.md")) + $"\n\n{Guid.NewGuid():N}\n";

        var id = await Administracao.IngerirEAguardarAsync(cliente, Markdown(texto), "faq.md", "interno", "material_interno", "1");

        await using var banco = _banco.Criar();
        Assert.All(await banco.Trechos.Where(t => t.DocumentoId == id).ToListAsync(Cancelamento), t => Assert.Equal("interno", t.Classificacao));
    }

    [Fact]
    public async Task Documento_desativado_deixa_de_ser_recuperado()
    {
        var cliente = Admin();
        var marcador = $"marcador{Guid.NewGuid():N}";
        var id = await Administracao.IngerirEAguardarAsync(cliente, Markdown($"# Nota\n\nTexto com o {marcador} para a busca.\n"), "nota.md", "interno", "material_interno", "1");
        await using var banco = _banco.Criar();
        var trechoId = await banco.Trechos.Where(t => t.DocumentoId == id).Select(t => t.Id).FirstAsync(Cancelamento);

        var desativar = await cliente.PostAsync(new Uri($"/api/v1/documentos/{id}/desativar", UriKind.Relative), null, Cancelamento);

        Assert.Equal(HttpStatusCode.NoContent, desativar.StatusCode);
        await using var escopo = _fabrica.Services.CreateAsyncScope();
        var consulta = escopo.ServiceProvider.GetRequiredService<ConsultaBase>();
        var busca = await consulta.BuscarTrechosAsync(
            new BuscarTrechosRequest(marcador, "guia_pratico_efd_icms_ipi", "c", Periodo: "2026-01", Classificacoes: [Classificacao.Interno]), Cancelamento);
        Assert.DoesNotContain(busca.Trechos, t => t.Documento.Id == id);
        await Assert.ThrowsAsync<TrechoNaoEncontradoException>(() => consulta.ObterTrechoAsync(new ObterTrechoRequest(trechoId, "c"), Cancelamento));
    }

    [Fact]
    public async Task Papel_sem_permissao_recebe_403_e_sem_token_401()
    {
        var analista = await Administracao.IngerirAsync(_fabrica.Cliente(FabricaApiRag.CriarToken(papeis: "analista")), GuiaUnico(), "guia.pdf");
        var anonimo = await Administracao.IngerirAsync(_fabrica.Cliente(null), GuiaUnico(), "guia.pdf");

        Assert.Equal(HttpStatusCode.Forbidden, analista.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    [Fact]
    public async Task Consumo_dos_embeddings_fica_registrado_so_quando_o_gerador_real_registra()
    {
        // O gerador falso não grava consumo: confirma que a ingestão não inventa registros (P-17).
        await using var banco = _banco.Criar();
        var antes = await banco.ConsumosEmbeddings.CountAsync(Cancelamento);

        await Administracao.IngerirEAguardarAsync(Admin(), GuiaUnico(), "guia.pdf");

        Assert.Equal(antes, await banco.ConsumosEmbeddings.CountAsync(Cancelamento));
    }
}
