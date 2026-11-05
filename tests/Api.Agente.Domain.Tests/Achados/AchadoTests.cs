using Api.Agente.Domain.Achados;

namespace Api.Agente.Domain.Tests.Achados;

public class AchadoTests
{
    private static readonly Fonte FonteGuia = new("Guia Prático da EFD-ICMS/IPI", "3.2.4", "Cap. II, Seção 1");

    [Fact]
    public void Fonte_exige_documento_versao_e_localizacao()
    {
        Assert.Throws<ArgumentException>(() => new Fonte("", "3.2.4", "Cap. II"));
        Assert.Throws<ArgumentException>(() => new Fonte("Guia", " ", "Cap. II"));
        Assert.Throws<ArgumentException>(() => new Fonte("Guia", "3.2.4", ""));
    }

    [Fact]
    public void Indicio_da_ia_nunca_e_impeditivo()
    {
        Assert.Throws<ArgumentException>(() =>
            new Achado("a1", Categoria.IndicioIa, Severidade.Impeditiva, Origem.Ia, "indício"));
    }

    [Fact]
    public void Indicio_da_ia_exige_origem_ia()
    {
        Assert.Throws<ArgumentException>(() =>
            new Achado("a1", Categoria.IndicioIa, Severidade.Advertencia, Origem.Validador, "indício"));
    }

    [Theory]
    [InlineData(Categoria.ErroEstrutural)]
    [InlineData(Categoria.InconsistenciaNegocio)]
    public void Achado_deterministico_exige_validador_regra_e_fonte(Categoria categoria)
    {
        Assert.Throws<ArgumentException>(() =>
            new Achado("a1", categoria, Severidade.Impeditiva, Origem.Ia, "erro", "EFD-ESTR-001", fonte: FonteGuia));
        Assert.Throws<ArgumentException>(() =>
            new Achado("a1", categoria, Severidade.Impeditiva, Origem.Validador, "erro", fonte: FonteGuia));
        Assert.Throws<ArgumentException>(() =>
            new Achado("a1", categoria, Severidade.Impeditiva, Origem.Validador, "erro", "EFD-ESTR-001"));
    }

    [Fact]
    public void Achado_deterministico_valido_guarda_a_localizacao()
    {
        var achado = new Achado(
            "a1", Categoria.ErroEstrutural, Severidade.Impeditiva, Origem.Validador, "falta K990",
            "EFD-ESTR-001", linha: 10, registro: "K990", fonte: FonteGuia);

        Assert.Equal(10, achado.Linha);
        Assert.Equal("K990", achado.Registro);
        Assert.Equal(FonteGuia, achado.Fonte);
    }

    [Fact]
    public void Falha_tecnica_nao_exige_regra_nem_fonte()
    {
        var achado = new Achado("a1", Categoria.FalhaTecnica, Severidade.Informativa, Origem.Validador, "base indisponível");

        Assert.Null(achado.RegraId);
    }
}
