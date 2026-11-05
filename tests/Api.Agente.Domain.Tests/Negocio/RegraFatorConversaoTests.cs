using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Tests.Estrutural;

namespace Api.Agente.Domain.Tests.Negocio;

public class RegraFatorConversaoTests
{
    private const string Regra = RegrasNegocio.FatorConversao;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static Task<string> TextoAsync() => FixturesEfd.LerTextoAsync("sem-0220", Cancelamento);

    [Fact]
    public async Task Aponta_o_item_e_a_unidade_sem_fator_de_conversao()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("sem-0220", Cancelamento);

        var achado = Assert.Single(resultado.Achados);
        Assert.Equal(Regra, achado.RegraId);
        Assert.Equal(Severidade.Impeditiva, achado.Severidade);
        Assert.Equal("C170", achado.Registro);
        Assert.Equal("CX", achado.ValorEncontrado);
        Assert.Equal("UN", achado.ValorEsperado);
        Assert.Contains("I1", achado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registro_0220_da_unidade_elimina_o_achado()
    {
        var comFator = FixturesEfd.Trocar(await TextoAsync(), "|0200|I1|ITEM FICTICIO|||UN|00|||||||\r\n", "|0200|I1|ITEM FICTICIO|||UN|00|||||||\r\n|0220|CX|10,000000||\r\n");

        var resultado = await FixturesEfd.ValidarAsync(FixturesEfd.ComoArquivo(comFator), Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.RegraId == Regra);
    }

    [Fact]
    public async Task Registro_0220_de_outra_unidade_nao_elimina_o_achado()
    {
        var comOutroFator = FixturesEfd.Trocar(await TextoAsync(), "|0200|I1|ITEM FICTICIO|||UN|00|||||||\r\n", "|0200|I1|ITEM FICTICIO|||UN|00|||||||\r\n|0220|PC|10,000000||\r\n");

        var resultado = await FixturesEfd.ValidarAsync(FixturesEfd.ComoArquivo(comOutroFator), Cancelamento);

        Assert.Contains(resultado.Achados, a => a.RegraId == Regra);
    }

    [Fact]
    public async Task Material_de_uso_e_consumo_e_dispensado()
    {
        var usoConsumo = FixturesEfd.Trocar(await TextoAsync(), "|0200|I1|ITEM FICTICIO|||UN|00|", "|0200|I1|ITEM FICTICIO|||UN|07|");

        var resultado = await FixturesEfd.ValidarAsync(FixturesEfd.ComoArquivo(usoConsumo), Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.RegraId == Regra);
    }

    [Fact]
    public async Task Item_fora_do_0200_nao_gera_achado_de_conversao()
    {
        var semItem = FixturesEfd.Trocar(await TextoAsync(), "|C170|1|I1|", "|C170|1|OUTRO|");

        var resultado = await FixturesEfd.ValidarAsync(FixturesEfd.ComoArquivo(semItem), Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.RegraId == Regra);
    }
}
