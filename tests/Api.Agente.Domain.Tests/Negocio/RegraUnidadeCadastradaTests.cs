using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Tests.Estrutural;

namespace Api.Agente.Domain.Tests.Negocio;

public class RegraUnidadeCadastradaTests
{
    private const string Regra = RegrasNegocio.UnidadeCadastrada;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Aponta_a_unidade_o_item_e_as_linhas_onde_e_usada()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("unidade-sem-0190", Cancelamento);

        var achados = resultado.Achados.Where(a => a.RegraId == Regra).ToList();
        Assert.Equal(2, achados.Count);
        Assert.All(achados, a =>
        {
            Assert.Equal(Categoria.InconsistenciaNegocio, a.Categoria);
            Assert.Equal(Severidade.Impeditiva, a.Severidade);
            Assert.Equal("UN", a.ValorEncontrado);
            Assert.NotNull(a.Linha);
            Assert.Contains("I1", a.Mensagem, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(a.Fonte!.Localizacao));
        });
    }

    [Fact]
    public async Task Unidade_de_inventario_tambem_e_verificada()
    {
        var texto = await FixturesEfd.LerTextoAsync("valido-perfil-a", Cancelamento);
        var comInventario = FixturesEfd.Trocar(texto, "|H001|1|\r\n|H990|", "|H001|0|\r\n|H005|31122025|100,00|01|\r\n|H010|I1|XX|1,000|100,00|100,00|0||||0,00|\r\n|H990|");

        var resultado = await FixturesEfd.ValidarAsync(FixturesEfd.ComoArquivo(comInventario), Cancelamento);

        Assert.Contains(resultado.Achados, a => a.RegraId == Regra && a.Registro == "H010" && a.ValorEncontrado == "XX");
    }

    [Fact]
    public async Task Unidade_cadastrada_nao_gera_achado()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("ficticio-completo-2026-01", Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.RegraId == Regra);
    }

    [Fact]
    public async Task Linha_com_quantidade_errada_de_campos_nao_gera_achado_de_unidade()
    {
        var texto = await FixturesEfd.LerTextoAsync("ficticio-completo-2026-01", Cancelamento);
        var malformado = FixturesEfd.Trocar(texto, "|C170|1|PROD001||100,00000|UN|", "|C170|1|PROD001||100,00000|XX|campo-a-mais|");

        var resultado = await FixturesEfd.ValidarAsync(FixturesEfd.ComoArquivo(malformado), Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.RegraId == Regra);
    }
}
