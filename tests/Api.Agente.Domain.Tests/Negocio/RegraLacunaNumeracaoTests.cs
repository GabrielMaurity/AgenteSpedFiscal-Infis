using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Catalogo;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Tests.Estrutural;

namespace Api.Agente.Domain.Tests.Negocio;

public class RegraLacunaNumeracaoTests
{
    private const string Regra = RegrasNegocio.LacunaNumeracao;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>Simula a regra confirmada em fonte oficial, para testar a lógica que hoje não executa.</summary>
    private static CatalogoRegras CatalogoComRegraAtiva()
    {
        var catalogo = FixturesEfd.Dados.Catalogo;
        var ativa = catalogo.Regras.Single(r => r.Id == Regra) with
        {
            Status = "ativa",
            Fonte = new Fonte("Documento de teste", "1", "p. 1"),
        };
        return catalogo with { Regras = [.. catalogo.Regras.Where(r => r.Id != Regra), ativa] };
    }

    [Fact]
    public void Regra_esta_pendente_no_catalogo_porque_nao_ha_fonte_oficial()
    {
        var regra = FixturesEfd.Dados.Catalogo.Regras.Single(r => r.Id == Regra);

        Assert.Equal("pendente_confirmacao", regra.Status);
        Assert.Null(regra.Fonte);
        Assert.False(string.IsNullOrWhiteSpace(regra.OrigemCandidata));
    }

    [Fact]
    public async Task Regra_pendente_nao_executa_e_fica_listada_como_nao_realizada()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("lacuna-numeracao-propria", Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.RegraId == Regra);
        Assert.DoesNotContain(Regra, resultado.RegrasExecutadas);
        Assert.Equal(Categoria.AusenciaEvidencias, Assert.Single(resultado.NaoRealizadas, n => n.Referencia == Regra).Categoria);
    }

    [Fact]
    public async Task Regra_ativa_aponta_o_intervalo_faltante_como_advertencia()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("lacuna-numeracao-propria", Cancelamento, catalogo: CatalogoComRegraAtiva());

        var achado = Assert.Single(resultado.Achados);
        Assert.Equal(Regra, achado.RegraId);
        Assert.Equal(Severidade.Advertencia, achado.Severidade);
        Assert.Equal("4", achado.ValorEncontrado);
        Assert.Contains("Modelo 55, série 1", achado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Regra_ativa_ignora_documentos_de_terceiros()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("lacuna-numeracao-terceiros", Cancelamento, catalogo: CatalogoComRegraAtiva());

        Assert.Empty(resultado.Achados);
    }

    [Fact]
    public async Task Regra_ativa_aceita_documento_cancelado_como_justificativa_da_sequencia()
    {
        // O fixture tem 1, 2, 3 (cancelado) e 5: o 3 não é lacuna.
        var resultado = await FixturesEfd.ValidarCasoAsync("lacuna-numeracao-propria", Cancelamento, catalogo: CatalogoComRegraAtiva());

        Assert.DoesNotContain(resultado.Achados, a => a.ValorEncontrado!.Contains('3', StringComparison.Ordinal));
    }
}
