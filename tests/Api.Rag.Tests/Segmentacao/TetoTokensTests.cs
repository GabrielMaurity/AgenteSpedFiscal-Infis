using Api.Rag.Domain.Segmentacao;

namespace Api.Rag.Tests.Segmentacao;

/// <summary>
/// Nenhum trecho passa do teto de tokens: acima do limite do modelo de embeddings (8.191), a OpenAI
/// recusa o lote com 400. Encontrado ao ingerir o Manual Consolidado MG, em que um "registro" abrange
/// dezenas de páginas (feature 002).
/// </summary>
public class TetoTokensTests
{
    private static readonly ParametrosSegmentacao Parametros = new(TetoTokens: 300);

    private static string Linha(int i) => $"Linha {i:D4} do caso prático com texto suficiente para ocupar espaço na estimativa de tokens.";

    private static IReadOnlyList<PaginaExtraida> Paginas(params string[][] linhasPorPagina) =>
        [.. linhasPorPagina.Select((linhas, i) => new PaginaExtraida(i + 1, linhas))];

    [Fact]
    public void Registro_enorme_sem_notas_de_campo_e_dividido_sem_perder_texto()
    {
        var linhas = Enumerable.Range(1, 120).Select(Linha).ToArray();
        var paginas = Paginas(["REGISTRO E111: AJUSTE DA APURAÇÃO", .. linhas[..60]], [.. linhas[60..]]);

        var trechos = SegmentadorGuia.Segmentar(paginas, Parametros);

        Assert.True(trechos.Count > 1);
        Assert.All(trechos, t => Assert.InRange(ParametrosSegmentacao.EstimarTokens(t.Texto), 1, Parametros.TetoTokens));
        Assert.All(trechos, t => Assert.Equal("E111", t.Localizacao.Registro));
        var juntado = string.Join("\n", trechos.Select(t => t.Texto));
        Assert.All(linhas, l => Assert.Contains(l, juntado, StringComparison.Ordinal));
        Assert.Equal(1, trechos[0].Localizacao.PaginaInicio);
        Assert.Equal(2, trechos[^1].Localizacao.PaginaFim);
    }

    [Fact]
    public void Nota_de_campo_gigante_tambem_e_dividida()
    {
        var nota = Enumerable.Range(1, 80).Select(Linha).ToArray();
        var paginas = Paginas(["REGISTRO C180: INFORMAÇÕES COMPLEMENTARES", "Nº Campo Descrição", "Campo 12 (VL_UNIT) - Preenchimento:", .. nota]);

        var trechos = SegmentadorGuia.Segmentar(paginas, Parametros);

        Assert.All(trechos, t => Assert.True(ParametrosSegmentacao.EstimarTokens(t.Texto) <= Parametros.TetoTokens));
        Assert.Contains(trechos, t => t.Texto.Contains(Linha(80), StringComparison.Ordinal));
    }

    [Fact]
    public void Linha_unica_maior_que_o_teto_e_cortada()
    {
        var gigante = string.Concat(Enumerable.Repeat("pontilhado do sumário . . . ", 200));
        var paginas = Paginas(["REGISTRO 0000: ABERTURA", gigante]);

        var trechos = SegmentadorGuia.Segmentar(paginas, Parametros);

        Assert.All(trechos, t => Assert.True(ParametrosSegmentacao.EstimarTokens(t.Texto) <= Parametros.TetoTokens));
        // Nenhum caractere visível se perde no corte (espaços nas bordas de cada parte podem ser aparados).
        static int Visiveis(string s) => s.Count(c => !char.IsWhiteSpace(c));
        Assert.Equal(Visiveis(gigante) + Visiveis("REGISTRO 0000: ABERTURA"), trechos.Sum(t => Visiveis(t.Texto)));
    }

    [Fact]
    public void Paragrafo_enorme_de_texto_generico_e_dividido()
    {
        var paragrafo = string.Join("\n", Enumerable.Range(1, 120).Select(Linha));

        var trechos = SegmentadorGenerico.Segmentar($"# Título\n{paragrafo}\n", Parametros);

        Assert.True(trechos.Count > 1);
        Assert.All(trechos, t => Assert.True(ParametrosSegmentacao.EstimarTokens(t.Texto) <= Parametros.TetoTokens));
    }

    [Fact]
    public void Teto_padrao_fica_abaixo_do_limite_do_modelo_e_acima_do_maior_trecho_do_guia()
    {
        // Maior trecho do Guia 3.2.4 ≈ 3.844 tokens estimados (registro C100); limite do modelo: 8.191.
        Assert.InRange(new ParametrosSegmentacao().TetoTokens, 3_844, 8_191 / 2);
    }
}
