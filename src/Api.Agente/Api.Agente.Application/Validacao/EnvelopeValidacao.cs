using System.Globalization;
using System.Text.Json.Serialization;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Analise;
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

public sealed record ReferenciaEnvelope(string TrechoId, string Documento, string Versao, string Localizacao, string Classificacao);

public sealed record ExplicacaoEnvelope(string AchadoId, string Texto, string CausaProvavel, string AcaoCorretiva, IReadOnlyList<string> TrechoIds);

public sealed record IndicioEnvelope(
    string Descricao,
    string Severidade,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Registro,
    IReadOnlyList<string> TrechoIds);

/// <summary>Instância de llm-analise.schema.json já validada.</summary>
public sealed record ConteudoAnaliseEnvelope(
    string VersaoContrato,
    string Resumo,
    IReadOnlyList<ExplicacaoEnvelope> Explicacoes,
    IReadOnlyList<IndicioEnvelope> Indicios,
    IReadOnlyList<string> Limitacoes,
    bool RevisaoHumanaRecomendada);

/// <summary>Conteúdo do LLM validado mais metadados do sistema (P-12). Conteúdo nulo quando a análise falhou.</summary>
public sealed record AnaliseIaEnvelope(
    string Status,
    string Modelo,
    int Tentativas,
    ConteudoAnaliseEnvelope? Conteudo,
    IReadOnlyList<ReferenciaEnvelope> Referencias,
    bool FundamentadaEmFonteOficial);

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
    AnaliseIaEnvelope? AnaliseIa,
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
            execucao.AnaliseIa is { } analise ? MapearAnalise(analise) : null,
            execucao.RevisaoHumanaNecessaria,
            AvisoPva)
        {
            MotivoFalha = execucao.MotivoFalha,
        };
    }

    public static AnaliseIaEnvelope MapearAnalise(AnaliseIa analise) => new(
        analise.Status == StatusAnaliseIa.Aceita ? "aceita" : "falha",
        analise.Modelo,
        analise.Tentativas,
        analise.Conteudo is { } c
            ? new ConteudoAnaliseEnvelope(
                c.VersaoContrato,
                c.Resumo,
                c.Explicacoes.Select(e => new ExplicacaoEnvelope(e.AchadoId, e.Texto, e.CausaProvavel, e.AcaoCorretiva, e.TrechoIds)).ToList(),
                c.Indicios.Select(i => new IndicioEnvelope(i.Descricao, ParaSeveridade(i.Severidade), i.Registro, i.TrechoIds)).ToList(),
                c.Limitacoes,
                c.RevisaoHumanaRecomendada)
            : null,
        analise.Referencias.Select(MapearReferencia).ToList(),
        analise.FundamentadaEmFonteOficial);

    public static ReferenciaEnvelope MapearReferencia(ReferenciaTrecho r) =>
        new(r.TrechoId, r.Documento, r.Versao, r.Localizacao, r.Classificacao == ClassificacaoFonte.Oficial ? "oficial" : "interno");

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
