using System.Globalization;
using System.Text;
using Api.Agente.Application.Validacao;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Application.Exportacao;

/// <summary>Relatório de achados em CSV, escrito em streaming (contrato exportacao-csv.md, FR-048).</summary>
public static class ExportadorCsv
{
    public const string TipoConteudo = "text/csv; charset=utf-8";

    public const string Cabecalho =
        "id;categoria;severidade;origem;linha;registro;campo;valorEncontrado;valorEsperado;regraId;fonteDocumento;fonteVersao;fonteLocalizacao;mensagem;acaoCorretiva";

    private const char Separador = ';';
    private const string FimDeLinha = "\r\n";

    private static readonly char[] CaracteresDeFormula = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] CaracteresComAspas = [Separador, '"', '\n', '\r'];

    public static async Task EscreverAsync(Execucao execucao, Stream destino, CancellationToken cancelamento)
    {
        var escritor = new StreamWriter(destino, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true) { NewLine = FimDeLinha };
        await using (escritor)
        {
            var envelope = EnvelopeMapper.Mapear(execucao);

            await EscreverMetadadosAsync(escritor, envelope);
            await escritor.WriteLineAsync(Cabecalho);

            foreach (var a in envelope.Achados)
            {
                cancelamento.ThrowIfCancellationRequested();
                await EscreverLinhaAsync(
                    escritor,
                    a.Id,
                    a.Categoria,
                    a.Severidade,
                    a.Origem,
                    a.Linha?.ToString(CultureInfo.InvariantCulture),
                    a.Registro,
                    a.Campo,
                    a.ValorEncontrado,
                    a.ValorEsperado,
                    a.RegraId,
                    a.Fonte?.Documento,
                    a.Fonte?.Versao,
                    a.Fonte?.Localizacao,
                    a.Mensagem,
                    a.AcaoCorretiva);
            }

            await EscreverTotaisAsync(escritor, execucao);
        }
    }

    /// <summary>Protege contra injeção de fórmulas e aplica as aspas da RFC 4180.</summary>
    public static string Formatar(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return "";
        }

        var seguro = Array.IndexOf(CaracteresDeFormula, valor[0]) >= 0 ? $"'{valor}" : valor;
        return seguro.IndexOfAny(CaracteresComAspas) >= 0
            ? $"\"{seguro.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : seguro;
    }

    private static async Task EscreverMetadadosAsync(StreamWriter escritor, EnvelopeValidacao e)
    {
        await Metadado(escritor, "versaoContrato", e.VersaoContrato);
        await Metadado(escritor, "execucaoId", e.ExecucaoId.ToString());
        await Metadado(escritor, "correlacaoId", e.CorrelacaoId);
        await Metadado(escritor, "status", e.Status);
        await Metadado(escritor, "criadoEm", e.CriadoEm.ToString("O", CultureInfo.InvariantCulture));
        await Metadado(escritor, "concluidoEm", e.ConcluidoEm?.ToString("O", CultureInfo.InvariantCulture));
        await Metadado(escritor, "arquivoNome", e.Arquivo?.Nome);
        await Metadado(escritor, "arquivoSha256", e.Arquivo?.Sha256);
        await Metadado(escritor, "codVer", e.Identificacao?.CodVer);
        await Metadado(escritor, "periodoInicio", e.Identificacao?.PeriodoInicio);
        await Metadado(escritor, "periodoFim", e.Identificacao?.PeriodoFim);
        await Metadado(escritor, "codFin", e.Identificacao?.CodFin);
        await Metadado(escritor, "uf", e.Identificacao?.Uf);
        await Metadado(escritor, "indPerfil", e.Identificacao?.IndPerfil);
        await Metadado(escritor, "cnpj", e.Identificacao?.Cnpj);
        await Metadado(escritor, "revisaoHumanaNecessaria", e.RevisaoHumanaNecessaria ? "true" : "false");
        await Metadado(escritor, "regrasExecutadas", string.Join(',', e.RegrasExecutadas));
        foreach (var v in e.ValidacoesNaoRealizadas)
        {
            await escritor.WriteLineAsync($"# validacaoNaoRealizada{Separador}{Formatar(v.Referencia)}{Separador}{v.Categoria}{Separador}{Formatar(v.Motivo)}");
        }

        foreach (var f in e.ForaDoAlcance)
        {
            await escritor.WriteLineAsync($"# foraDoAlcance{Separador}{Formatar(f.Referencia)}{Separador}{f.Categoria}{Separador}{Formatar(f.Motivo)}");
        }

        await Metadado(escritor, "avisoPva", e.AvisoPva);
    }

    private static async Task Metadado(StreamWriter escritor, string chave, string? valor) =>
        await escritor.WriteLineAsync($"# {chave}{Separador}{Formatar(valor)}");

    private static async Task EscreverLinhaAsync(StreamWriter escritor, params string?[] valores) =>
        await escritor.WriteLineAsync(string.Join(Separador, valores.Select(Formatar)));

    private static async Task EscreverTotaisAsync(StreamWriter escritor, Execucao execucao)
    {
        var detalhados = execucao.Achados.Where(a => a.RegraId is not null).GroupBy(a => a.RegraId!).ToDictionary(g => g.Key, g => g.LongCount());
        var excedentes = execucao.TotaisPorRegra.Where(t => t.Value > detalhados.GetValueOrDefault(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        if (excedentes.Count == 0)
        {
            return;
        }

        await escritor.WriteLineAsync();
        await escritor.WriteLineAsync("# totais");
        foreach (var (regra, total) in excedentes)
        {
            await escritor.WriteLineAsync($"# total{Separador}{regra}{Separador}{total}{Separador}{detalhados.GetValueOrDefault(regra)}");
        }
    }
}
