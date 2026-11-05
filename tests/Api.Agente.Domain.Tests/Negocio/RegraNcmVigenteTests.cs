using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Tests.Estrutural;

namespace Api.Agente.Domain.Tests.Negocio;

public class RegraNcmVigenteTests
{
    private const string Regra = RegrasNegocio.NcmVigente;

    private static readonly DateOnly Inicio = new(2026, 1, 1);
    private static readonly DateOnly Fim = new(2026, 1, 31);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static TabelaNcm Tabela(params CodigoNcm[] codigos) => new("2026.1", codigos, Inicio, Fim);

    [Fact]
    public async Task Sem_tabela_a_verificacao_fica_nao_realizada_como_falha_tecnica()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("ncm-inexistente", Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.RegraId == Regra);
        Assert.DoesNotContain(Regra, resultado.RegrasExecutadas);
        var naoRealizada = Assert.Single(resultado.NaoRealizadas, n => n.Referencia == Regra);
        Assert.Equal(Categoria.FalhaTecnica, naoRealizada.Categoria);
    }

    [Fact]
    public async Task Ncm_inexistente_na_tabela_e_apontado_com_item_e_versao()
    {
        var gabarito = FixturesEfd.LerGabarito("ncm-inexistente");

        var resultado = await FixturesEfd.ValidarCasoAsync("ncm-inexistente", Cancelamento, Tabela(new CodigoNcm("32041700", null, null)));

        var achado = Assert.Single(resultado.Achados);
        Assert.True(FixturesEfd.Corresponde(achado, Assert.Single(gabarito.Achados!)), FixturesEfd.Descrever(resultado.Achados));
        Assert.Equal(Severidade.Advertencia, achado.Severidade);
        Assert.Equal("99999999", achado.ValorEncontrado);
        Assert.Contains("I1", achado.Mensagem, StringComparison.Ordinal);
        Assert.Contains("2026.1", achado.Mensagem, StringComparison.Ordinal);
        Assert.Contains(Regra, resultado.RegrasExecutadas);
        Assert.DoesNotContain(resultado.NaoRealizadas, n => n.Referencia == Regra);
    }

    [Fact]
    public async Task Ncm_fora_da_vigencia_e_apontado_como_nao_vigente()
    {
        var tabela = Tabela(new CodigoNcm("99999999", new DateOnly(2020, 1, 1), new DateOnly(2025, 12, 31)));

        var resultado = await FixturesEfd.ValidarCasoAsync("ncm-inexistente", Cancelamento, tabela);

        Assert.Contains("não está vigente", Assert.Single(resultado.Achados).Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ncm_vigente_nao_gera_achado()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("ncm-inexistente", Cancelamento, Tabela(new CodigoNcm("99999999", Inicio, null)));

        Assert.Empty(resultado.Achados);
    }

    [Fact]
    public async Task Item_sem_ncm_nao_gera_achado()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("valido-perfil-a", Cancelamento, Tabela(new CodigoNcm("32041700", null, null)));

        Assert.Empty(resultado.Achados);
    }

    [Fact]
    public void Codigo_com_mais_de_um_intervalo_vale_se_algum_cobrir_o_periodo()
    {
        var tabela = Tabela(
            new CodigoNcm("11111111", new DateOnly(2026, 1, 15), null),
            new CodigoNcm("11111111", new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1)));

        Assert.Equal(SituacaoNcm.Vigente, tabela.Consultar("11111111"));
        Assert.Equal(SituacaoNcm.Inexistente, tabela.Consultar("22222222"));
    }
}
