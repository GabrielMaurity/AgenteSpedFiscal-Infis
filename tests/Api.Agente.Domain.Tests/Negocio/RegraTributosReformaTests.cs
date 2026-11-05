using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Tests.Estrutural;

namespace Api.Agente.Domain.Tests.Negocio;

public class RegraTributosReformaTests
{
    private const string Regra = RegrasNegocio.TributosReforma;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Vl_doc_de_2026_sem_cbs_ibs_nao_gera_achado()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("cbs-ibs-2026", Cancelamento);

        Assert.Empty(resultado.Achados);
    }

    [Fact]
    public async Task Verificacao_do_vl_opr_fica_como_ausencia_de_evidencias_e_nao_como_aprovada()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("cbs-ibs-2026", Cancelamento);

        var naoRealizada = Assert.Single(resultado.NaoRealizadas, n => n.Referencia == Regra);
        Assert.Equal(Categoria.AusenciaEvidencias, naoRealizada.Categoria);
        Assert.DoesNotContain(Regra, resultado.RegrasExecutadas);
    }

    [Fact]
    public async Task Arquivo_sem_registro_analitico_nao_tem_o_que_verificar()
    {
        var texto = await FixturesEfd.LerTextoAsync("valido-perfil-a", Cancelamento);
        var linhas = texto.Split("\r\n").Where(l => !l.StartsWith("|C190|", StringComparison.Ordinal));

        var resultado = await FixturesEfd.ValidarAsync(FixturesEfd.ComoArquivo(string.Join("\r\n", linhas)), Cancelamento);

        Assert.DoesNotContain(resultado.NaoRealizadas, n => n.Referencia == Regra);
    }

    [Fact]
    public void Regra_de_2027_fica_pendente_e_nao_se_aplica_ao_leiaute_020()
    {
        var regra = FixturesEfd.Dados.Catalogo.Regras.Single(r => r.Id == "EFD-NEG-010");

        Assert.Equal("pendente_confirmacao", regra.Status);
        Assert.False(regra.SeAplica("020", new DateOnly(2026, 12, 31), "A", "RJ"));
    }
}
