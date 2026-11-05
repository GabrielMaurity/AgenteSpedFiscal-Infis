using System.Text.Json;
using AgenteSpedFiscal.Contracts.Mcp;

namespace Contracts.Tests;

public class ContratoMcpTests
{
    private static BuscarTrechosRequest Requisicao(
        string consulta = "C190", string? versao = "3.2.4", string? periodo = null, int? topK = null) =>
        new(consulta, "guia_pratico_efd_icms_ipi", "correlacao", versao, periodo, null, topK);

    [Fact]
    public void Requisicao_com_versao_e_valida() => Assert.Null(Requisicao().Validar());

    [Fact]
    public void Requisicao_com_periodo_e_valida() => Assert.Null(Requisicao(versao: null, periodo: "2026-03").Validar());

    [Fact]
    public void Exige_exatamente_um_entre_versao_e_periodo()
    {
        Assert.NotNull(Requisicao(versao: null).Validar());
        Assert.NotNull(Requisicao(periodo: "2026-03").Validar());
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026/03")]
    [InlineData("26-03")]
    public void Rejeita_periodo_fora_do_formato(string periodo) =>
        Assert.NotNull(Requisicao(versao: null, periodo: periodo).Validar());

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Rejeita_top_k_fora_do_intervalo(int topK) => Assert.NotNull(Requisicao(topK: topK).Validar());

    [Fact]
    public void Rejeita_consulta_vazia_ou_longa()
    {
        Assert.NotNull(Requisicao(consulta: " ").Validar());
        Assert.NotNull(Requisicao(consulta: new string('a', 1_001)).Validar());
    }

    [Theory]
    [InlineData(Classificacao.Oficial, "\"oficial\"")]
    [InlineData(Classificacao.Interno, "\"interno\"")]
    public void Classificacao_usa_o_valor_do_contrato(Classificacao classificacao, string esperado) =>
        Assert.Equal(esperado, JsonSerializer.Serialize(classificacao));
}
