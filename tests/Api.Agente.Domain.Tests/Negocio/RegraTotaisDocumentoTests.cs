using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Tests.Estrutural;

namespace Api.Agente.Domain.Tests.Negocio;

public class RegraTotaisDocumentoTests
{
    private const string NotaEntrada = "|4500,00|1|0,00|0,00|4500,00|0|0,00|0,00|0,00|4500,00|540,00|";
    private const string Analitico2102 = "|C190|000|2102|12,00|4500,00|4500,00|540,00|";
    private const string Item1 = "|2500,00|12,00|300,00|";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static async Task<IReadOnlyList<Achado>> ValidarComAsync(string de, string para)
    {
        var texto = FixturesEfd.Trocar(await FixturesEfd.LerTextoAsync("ficticio-completo-2026-01", Cancelamento), de, para);
        return (await FixturesEfd.ValidarAsync(FixturesEfd.ComoArquivo(texto), Cancelamento)).Achados;
    }

    [Fact]
    public async Task Divergencias_informam_valores_comparados_e_diferenca()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("c100-divergente", Cancelamento);

        var documento = Assert.Single(resultado.Achados, a => a.RegraId == RegrasNegocio.ValorDocumento);
        Assert.Equal("150,00", documento.ValorEncontrado);
        Assert.Equal("100,00", documento.ValorEsperado);
        Assert.Contains("diferença de 50,00", documento.Mensagem, StringComparison.Ordinal);
        Assert.Equal(Severidade.Advertencia, documento.Severidade);

        var analitico = Assert.Single(resultado.Achados, a => a.RegraId == RegrasNegocio.TotaisAnaliticos);
        Assert.Equal("VL_BC_ICMS", analitico.Campo);
        Assert.Equal(Severidade.Impeditiva, analitico.Severidade);

        var mercadorias = Assert.Single(resultado.Achados, a => a.RegraId == RegrasNegocio.ValorMercadorias);
        Assert.Equal("120,00", mercadorias.ValorEncontrado);
    }

    [Fact]
    public async Task Soma_dos_c170_diferente_do_c190_na_mesma_combinacao_e_apontada()
    {
        var achados = await ValidarComAsync(Item1, "|2500,00|12,00|290,00|");

        var achado = Assert.Single(achados);
        Assert.Equal(RegrasNegocio.ItensAnaliticos, achado.RegraId);
        Assert.Equal("C190", achado.Registro);
        Assert.Equal("VL_ICMS", achado.Campo);
        Assert.Equal("540,00", achado.ValorEncontrado);
        Assert.Equal("530,00", achado.ValorEsperado);
    }

    [Fact]
    public async Task Combinacao_do_c190_sem_c170_e_apontada()
    {
        var achados = await ValidarComAsync(Analitico2102, "|C190|000|2102|7,00|4500,00|4500,00|540,00|");

        Assert.Single(achados, a => a.RegraId == RegrasNegocio.ItensAnaliticos && a.Mensagem.Contains("não tem C170", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Aliquota_e_comparada_como_numero()
    {
        var achados = await ValidarComAsync(Analitico2102, "|C190|000|2102|12|4500,00|4500,00|540,00|");

        Assert.Empty(achados);
    }

    [Fact]
    public async Task Nfe_propria_sem_c170_nao_e_comparada_com_os_itens()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("cbs-ibs-2026", Cancelamento);

        Assert.Empty(resultado.Achados);
    }

    [Fact]
    public async Task Nfe_propria_com_c170_parcial_nao_gera_divergencia_de_itens()
    {
        // Guia, C100, Exceção 6: o C170 traz só os itens com direito a ressarcimento.
        var texto = FixturesEfd.Trocar(
            await FixturesEfd.LerTextoAsync("cbs-ibs-2026", Cancelamento),
            "|C190|000|5102|",
            "|C170|1|I1||1,00000|UN|10,00||0|000|5102||10,00|18,00|1,80|||||||||||||||||||||||\r\n|C190|000|5102|");

        var resultado = await FixturesEfd.ValidarAsync(FixturesEfd.ComoArquivo(texto), Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.RegraId is RegrasNegocio.ValorMercadorias or RegrasNegocio.ItensAnaliticos);
    }

    [Fact]
    public async Task Nota_cancelada_nao_tem_totais_comparados()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("ficticio-completo-2026-01", Cancelamento);

        Assert.Empty(resultado.Achados);
    }

    [Fact]
    public async Task Linha_de_item_malformada_nao_gera_divergencia_em_cascata()
    {
        var achados = await ValidarComAsync("|C170|1|PROD001||100,00000|UN|", "|C170|1|PROD001||100,00000|campo-a-mais|UN|");

        var achado = Assert.Single(achados);
        Assert.Equal("EFD-ESTR-004", achado.RegraId);
    }

    [Fact]
    public async Task Valor_invalido_em_filho_nao_gera_divergencia_em_cascata()
    {
        var achados = await ValidarComAsync(Item1, "|2500,00|12,00|3x0,00|");

        Assert.DoesNotContain(achados, a => a.RegraId is RegrasNegocio.ItensAnaliticos or RegrasNegocio.TotaisAnaliticos);
        Assert.Contains(achados, a => a.RegraId == "EFD-ESTR-006");
    }

    [Fact]
    public async Task Valor_informado_igual_a_soma_em_outra_escrita_nao_gera_achado()
    {
        var achados = await ValidarComAsync(NotaEntrada, "|4500|1|0,00|0,00|4500,0|0|0,00|0,00|0,00|4500,00|540,00|");

        Assert.Empty(achados);
    }
}
