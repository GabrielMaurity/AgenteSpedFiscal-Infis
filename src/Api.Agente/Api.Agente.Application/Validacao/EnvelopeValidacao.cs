using System.Globalization;
using System.Text.Json.Serialization;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Application.Validacao;

public sealed record EtapaEnvelope(int Numero, string Nome, string Status, string? Motivo);

public sealed record ValidacaoNaoRealizadaEnvelope(string Referencia, string Categoria, string Motivo);

public sealed record ArquivoEnvelope(string Nome, long TamanhoBytes, string Sha256);

public sealed record IdentificacaoEnvelope(string CodVer, string PeriodoInicio, string PeriodoFim, string CodFin, string Uf, string Cnpj, string IndPerfil);

public sealed record FonteEnvelope(string Documento, string Versao, string Localizacao);

public sealed record AchadoEnvelope(
    string Id,
    string Categoria,
    string Severidade,
    string Origem,
    string? RegraId,
    long? Linha,
    string? Registro,
    string? Campo,
    string? ValorEncontrado,
    string? ValorEsperado,
    string Mensagem,
    string? AcaoCorretiva,
    FonteEnvelope? Fonte);

/// <summary>Contrato envelope-validacao.schema.json. Todos os campos são preenchidos pelo sistema (P-12).</summary>
public sealed record EnvelopeValidacao(
    string VersaoContrato,
    Guid ExecucaoId,
    string CorrelacaoId,
    string Status,
    DateTimeOffset CriadoEm,
    DateTimeOffset? ConcluidoEm,
    long? DuracaoMs,
    ArquivoEnvelope? Arquivo,
    IdentificacaoEnvelope? Identificacao,
    IReadOnlyList<EtapaEnvelope> Etapas,
    IReadOnlyList<string> RegrasExecutadas,
    IReadOnlyList<ValidacaoNaoRealizadaEnvelope> ValidacoesNaoRealizadas,
    IReadOnlyList<ValidacaoNaoRealizadaEnvelope> ForaDoAlcance,
    IReadOnlyList<AchadoEnvelope> Achados,
    IReadOnlyDictionary<string, long> TotaisPorRegra,
    bool RevisaoHumanaNecessaria,
    string AvisoPva)
{
    [JsonIgnore]
    public string? MotivoFalha { get; init; }
}

public static class EnvelopeMapper
{
    public const string VersaoContrato = "1.1.0";

    public const string AvisoPva =
        "Sem achados nas regras implementadas não equivale a conformidade. A validação oficial do PVA permanece obrigatória.";

    /// <summary>Mesma ordem na API e no CSV: por linha e, em seguida, por regra (exportacao-csv.md).</summary>
    public static IEnumerable<Achado> Ordenar(IEnumerable<Achado> achados) =>
        achados.OrderBy(a => a.Linha ?? long.MaxValue).ThenBy(a => a.RegraId, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal);

    public static EnvelopeValidacao Mapear(Execucao execucao, int pagina = 1, int tamanhoPagina = int.MaxValue)
    {
        var achados = Ordenar(execucao.Achados)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .Select(MapearAchado)
            .ToList();
        var finalizada = execucao.EstaFinalizada;

        return new EnvelopeValidacao(
            VersaoContrato,
            execucao.Id,
            execucao.CorrelacaoId,
            ParaStatus(execucao.Estado),
            execucao.CriadoEm,
            finalizada ? execucao.AtualizadoEm : null,
            finalizada ? (long)(execucao.AtualizadoEm - execucao.CriadoEm).TotalMilliseconds : null,
            execucao.Arquivo is { } a ? new ArquivoEnvelope(a.Nome, a.TamanhoBytes, a.Sha256) : null,
            execucao.Identificacao is { } i ? MapearIdentificacao(i) : null,
            execucao.Etapas.Select(e => new EtapaEnvelope(e.Numero, e.Nome, ParaStatusEtapa(e.Status), e.Motivo)).ToList(),
            execucao.RegrasExecutadas,
            execucao.ValidacoesNaoRealizadas.Select(v => new ValidacaoNaoRealizadaEnvelope(v.Referencia, ParaCategoriaNaoRealizada(v.Categoria), v.Motivo)).ToList(),
            execucao.ForaDoAlcance.Select(v => new ValidacaoNaoRealizadaEnvelope(v.Referencia, ParaCategoriaNaoRealizada(v.Categoria), v.Motivo)).ToList(),
            achados,
            execucao.TotaisPorRegra,
            execucao.RevisaoHumanaNecessaria,
            AvisoPva)
        {
            MotivoFalha = execucao.MotivoFalha,
        };
    }

    public static AchadoEnvelope MapearAchado(Achado a) => new(
        a.Id,
        ParaCategoria(a.Categoria),
        ParaSeveridade(a.Severidade),
        a.Origem == Origem.Validador ? "validador" : "ia",
        a.RegraId,
        a.Linha,
        a.Registro,
        a.Campo,
        a.ValorEncontrado,
        a.ValorEsperado,
        a.Mensagem,
        a.AcaoCorretiva,
        a.Fonte is { } f ? new FonteEnvelope(f.Documento, f.Versao, f.Localizacao) : null);

    public static string ParaStatus(EstadoExecucao estado) => estado switch
    {
        EstadoExecucao.Recebida => "recebida",
        EstadoExecucao.Concluida => "concluida",
        EstadoExecucao.ConcluidaComLimitacoes => "concluida_com_limitacoes",
        EstadoExecucao.Falha => "falha",
        _ => "em_processamento",
    };

    public static string ParaCategoria(Categoria categoria) => categoria switch
    {
        Categoria.ErroEstrutural => "erro_estrutural",
        Categoria.InconsistenciaNegocio => "inconsistencia_negocio",
        Categoria.IndicioIa => "indicio_ia",
        Categoria.AusenciaEvidencias => "ausencia_evidencias",
        _ => "falha_tecnica",
    };

    private static string ParaCategoriaNaoRealizada(Categoria categoria) =>
        categoria == Categoria.AusenciaEvidencias ? "ausencia_evidencias" : "falha_tecnica";

    public static string ParaSeveridade(Severidade severidade) => severidade switch
    {
        Severidade.Impeditiva => "impeditiva",
        Severidade.Advertencia => "advertencia",
        _ => "informativa",
    };

    private static string ParaStatusEtapa(StatusEtapa status) => status switch
    {
        StatusEtapa.Pendente => "pendente",
        StatusEtapa.Executando => "executando",
        StatusEtapa.Concluida => "concluida",
        StatusEtapa.Falha => "falha",
        _ => "nao_executada",
    };

    private static IdentificacaoEnvelope MapearIdentificacao(IdentificacaoArquivo i) => new(
        i.CodVer,
        i.PeriodoInicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        i.PeriodoFim.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        i.CodFin,
        i.Uf,
        i.Cnpj,
        i.IndPerfil);
}
