using System.Text.RegularExpressions;
using Api.Rag.Domain.Segmentacao;

namespace Api.Rag.Tests.Segmentacao;

/// <summary>Páginas 47, 48, 106 e 107 do Guia Prático 3.2.4, com cabeçalho e rodapé (tests/fixtures/base).</summary>
public partial class SegmentadorGuiaTests
{
    private static readonly ParametrosSegmentacao Parametros = new();

    private static IReadOnlyList<PaginaExtraida> PaginasAmostra()
    {
        var texto = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "base", "guia-3.2.4-amostra.txt"));
        return [.. texto.Split('\f').Select(p =>
        {
            var linhas = p.Split('\n');
            var numero = int.Parse(linhas.Select(l => NumeroPagina().Match(l)).First(m => m.Success).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return new PaginaExtraida(numero, linhas);
        })];
    }

    private static Domain.Documentos.Trecho Principal(string registro) =>
        SegmentadorGuia.Segmentar(PaginasAmostra(), Parametros).First(t => t.Localizacao.Registro == registro);

    [Fact]
    public void Registro_gera_trecho_com_a_tabela_de_campos_inteira()
    {
        var c190 = Principal("C190");

        Assert.StartsWith("REGISTRO C190:", c190.Texto, StringComparison.Ordinal);
        Assert.Contains("01 REG", c190.Texto, StringComparison.Ordinal);
        Assert.Contains("12 COD_OBS", c190.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Campo 01 (REG)", c190.Texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Trecho_informa_pagina_inicial_e_final()
    {
        var c190 = Principal("C190");
        var r0990 = SegmentadorGuia.Segmentar(PaginasAmostra(), Parametros).Where(t => t.Localizacao.Registro == "0990").ToList();

        // A tabela do C190 atravessa a quebra de página.
        Assert.Equal(106, c190.Localizacao.PaginaInicio);
        Assert.Equal(107, c190.Localizacao.PaginaFim);

        // A tabela do 0990 cabe na página 47; a nota do campo 02 está na 48.
        Assert.Equal((47, 47), (r0990[0].Localizacao.PaginaInicio, r0990[0].Localizacao.PaginaFim));
        Assert.Contains(r0990.Skip(1), t => t.Localizacao.PaginaFim == 48 && t.Texto.Contains("QTD_LIN_0", StringComparison.Ordinal));
    }

    [Fact]
    public void Cabecalho_e_rodape_sao_removidos()
    {
        var trechos = SegmentadorGuia.Segmentar(PaginasAmostra(), Parametros);

        Assert.All(trechos, t =>
        {
            Assert.DoesNotContain("Guia Prático EFD-ICMS/IPI – Versão 3.2.4", t.Texto, StringComparison.Ordinal);
            Assert.DoesNotContain("Atualização: 30 de julho de 2026", t.Texto, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"Página \d+ de 366", t.Texto);
        });
    }

    [Fact]
    public void Notas_de_campo_ficam_em_trechos_do_mesmo_registro()
    {
        var trechos = SegmentadorGuia.Segmentar(PaginasAmostra(), Parametros).Where(t => t.Localizacao.Registro == "C190").ToList();

        Assert.True(trechos.Count >= 2);
        Assert.Contains(trechos.Skip(1), t => t.Texto.Contains("Campo 05 (VL_OPR)", StringComparison.Ordinal));
    }

    [Fact]
    public void Notas_longas_respeitam_o_limite_e_repetem_um_paragrafo()
    {
        var pequeno = new ParametrosSegmentacao(LimiteTokens: 120, SobreposicaoParagrafos: 1);
        var notas = SegmentadorGuia.Segmentar(PaginasAmostra(), pequeno)
            .Where(t => t.Localizacao.Registro == "C190" && t.Texto.StartsWith("Campo", StringComparison.Ordinal))
            .ToList();

        Assert.True(notas.Count >= 2);
        var primeiraLinhaDoSegundo = notas[1].Texto.Split('\n')[0];
        Assert.Contains(primeiraLinhaDoSegundo, notas[0].Texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordem_e_sequencial_e_nao_ha_trecho_repetido()
    {
        var trechos = SegmentadorGuia.Segmentar(PaginasAmostra(), Parametros);

        Assert.Equal(Enumerable.Range(1, trechos.Count), trechos.Select(t => t.Ordem));
        Assert.Equal(trechos.Count, trechos.Select(t => t.TextoHash).Distinct().Count());
    }

    [Fact]
    public void Capitulo_e_bloco_do_leiaute_entram_na_localizacao()
    {
        PaginaExtraida[] paginas =
        [
            new(10, ["Capítulo I – Introdução", "Seção 10 – Informações sobre a Reforma Tributária", "Texto da seção."]),
            new(63, ["Capítulo III – Registros", "BLOCO C: DOCUMENTOS FISCAIS I – MERCADORIAS (ICMS/IPI)", "REGISTRO C001: ABERTURA DO BLOCO C", "01 REG Texto fixo"]),
        ];

        var c001 = SegmentadorGuia.Segmentar(paginas, Parametros).Single(t => t.Localizacao.Registro == "C001");

        Assert.Equal("III", c001.Localizacao.Capitulo);
        Assert.Equal("Bloco C", c001.Localizacao.Secao);
    }

    [Fact]
    public void Material_interno_e_segmentado_por_titulos()
    {
        var texto = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "base", "faq-interno.md"));

        var trechos = SegmentadorGenerico.Segmentar(texto, Parametros);

        Assert.Contains(trechos, t => t.Localizacao.Secao == "Prazo interno de conferência" && t.Texto.Contains("dia 5", StringComparison.Ordinal));
        Assert.All(trechos, t => Assert.Null(t.Localizacao.PaginaInicio));
    }

    [GeneratedRegex(@"^Página (\d+) de 366")]
    private static partial Regex NumeroPagina();
}
