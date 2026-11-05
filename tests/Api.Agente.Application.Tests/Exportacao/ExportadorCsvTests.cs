using System.Text;
using Api.Agente.Application.Exportacao;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Application.Tests.Exportacao;

public class ExportadorCsvTests
{
    private static Execucao Execucao()
    {
        var execucao = Domain.Execucoes.Execucao.Criar(Guid.NewGuid(), "org", "u", "corr", CanalExecucao.Rest, DateTimeOffset.UtcNow);
        var fonte = new Fonte("Guia", "3.2.4", "Cap. II; p. 7");
        execucao.AplicarResultado(
            [
                new Achado("b", Categoria.ErroEstrutural, Severidade.Impeditiva, Origem.Validador, "=SOMA(A1)", "EFD-ESTR-002", 20, "K990", fonte: fonte),
                new Achado("a", Categoria.ErroEstrutural, Severidade.Impeditiva, Origem.Validador, "tem \"aspas\"", "EFD-ESTR-001", 10, "C100", fonte: fonte),
            ],
            new Dictionary<string, long> { ["EFD-ESTR-001"] = 1, ["EFD-ESTR-002"] = 5 },
            ["EFD-ESTR-001", "EFD-ESTR-002"],
            [new ValidacaoNaoRealizada("etapa-5", Categoria.AusenciaEvidencias, "base; indisponível")]);
        execucao.Concluir(analiseIaAceita: false);
        return execucao;
    }

    private static async Task<(byte[] Bytes, string[] Linhas)> GerarAsync(Execucao execucao)
    {
        using var destino = new MemoryStream();
        await ExportadorCsv.EscreverAsync(execucao, destino, TestContext.Current.CancellationToken);
        var bytes = destino.ToArray();
        return (bytes, new UTF8Encoding(true).GetString(bytes).TrimStart('﻿').Split("\r\n"));
    }

    [Fact]
    public async Task Usa_utf8_com_bom_e_crlf()
    {
        var (bytes, linhas) = await GerarAsync(Execucao());

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.True(linhas.Length > 5);
    }

    [Fact]
    public async Task Metadados_vem_antes_do_cabecalho_exato()
    {
        var (_, linhas) = await GerarAsync(Execucao());

        var cabecalho = Array.IndexOf(linhas, ExportadorCsv.Cabecalho);
        Assert.True(cabecalho > 0);
        Assert.All(linhas[..cabecalho], l => Assert.StartsWith("# ", l, StringComparison.Ordinal));
        Assert.Contains(linhas, l => l.StartsWith("# status;concluida_com_limitacoes", StringComparison.Ordinal));
        Assert.Contains(linhas, l => l.StartsWith("# avisoPva;", StringComparison.Ordinal));
        Assert.Contains(linhas, l => l.StartsWith("# validacaoNaoRealizada;etapa-5;ausencia_evidencias;\"base; indisponível\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Achados_seguem_a_ordem_por_linha_e_totais_vem_ao_final()
    {
        var (_, linhas) = await GerarAsync(Execucao());

        var cabecalho = Array.IndexOf(linhas, ExportadorCsv.Cabecalho);
        Assert.StartsWith("a;", linhas[cabecalho + 1], StringComparison.Ordinal);
        Assert.StartsWith("b;", linhas[cabecalho + 2], StringComparison.Ordinal);
        Assert.Contains("# totais", linhas);
        Assert.Contains("# total;EFD-ESTR-002;5;1", linhas);
        Assert.DoesNotContain("# total;EFD-ESTR-001;1;1", linhas);
    }

    [Theory]
    [InlineData("=SOMA(A1)", "'=SOMA(A1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@x", "'@x")]
    [InlineData("\tx", "'\tx")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("texto", "texto")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Protege_contra_formulas_e_aplica_aspas(string? valor, string esperado) =>
        Assert.Equal(esperado, ExportadorCsv.Formatar(valor));
}
