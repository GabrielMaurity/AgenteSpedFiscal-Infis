using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Tests.Estrutural;

namespace Api.Agente.Domain.Tests.Negocio;

/// <summary>
/// Regras pendentes de fonte e verificações que dependem de dado fora do arquivo vão para uma lista
/// própria, separada das validações não realizadas (FR-044, research R-30).
/// </summary>
public class ForaDoAlcanceTests
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Regra_pendente_de_fonte_fica_fora_do_alcance()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("lacuna-numeracao-propria", Cancelamento);

        var item = Assert.Single(resultado.ForaDoAlcance, f => f.Referencia == RegrasNegocio.LacunaNumeracao);
        Assert.Equal(Categoria.AusenciaEvidencias, item.Categoria);
        Assert.DoesNotContain(resultado.NaoRealizadas, n => n.Referencia == RegrasNegocio.LacunaNumeracao);
    }

    [Fact]
    public async Task Verificacao_que_depende_do_xml_fica_fora_do_alcance()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("cbs-ibs-2026", Cancelamento);

        var item = Assert.Single(resultado.ForaDoAlcance, f => f.Referencia == RegrasNegocio.TributosReforma);
        Assert.Equal(Categoria.AusenciaEvidencias, item.Categoria);
        Assert.DoesNotContain(resultado.NaoRealizadas, n => n.Referencia == RegrasNegocio.TributosReforma);
    }

    [Fact]
    public async Task Tabela_de_ncm_ausente_continua_como_validacao_nao_realizada()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("ncm-inexistente", Cancelamento);

        var naoRealizada = Assert.Single(resultado.NaoRealizadas);
        Assert.Equal(RegrasNegocio.NcmVigente, naoRealizada.Referencia);
        Assert.Equal(Categoria.FalhaTecnica, naoRealizada.Categoria);
        Assert.DoesNotContain(resultado.ForaDoAlcance, f => f.Referencia == RegrasNegocio.NcmVigente);
    }

    [Fact]
    public async Task Nenhuma_regra_aparece_em_duas_listas_nem_entre_as_executadas()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("cbs-ibs-2026", Cancelamento);

        var fora = resultado.ForaDoAlcance.Select(f => f.Referencia).ToList();
        var naoRealizadas = resultado.NaoRealizadas.Select(n => n.Referencia).ToList();
        Assert.Empty(fora.Intersect(naoRealizadas));
        Assert.Empty(fora.Intersect(resultado.RegrasExecutadas));
        Assert.Empty(naoRealizadas.Intersect(resultado.RegrasExecutadas));
        Assert.Equal(fora.Count, fora.Distinct().Count());
    }

    [Fact]
    public async Task Com_tabela_de_ncm_nao_ha_validacao_nao_realizada()
    {
        var tabela = new TabelaNcm("2026.1", [], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var resultado = await FixturesEfd.ValidarCasoAsync("cbs-ibs-2026", Cancelamento, tabela);

        Assert.Empty(resultado.NaoRealizadas);
        Assert.NotEmpty(resultado.ForaDoAlcance);
    }
}
