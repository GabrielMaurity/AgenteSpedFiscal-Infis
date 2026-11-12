using System.Collections.Concurrent;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Analise;

namespace Api.Agente.Application.Analise;

/// <summary>Busca na base de informação. Exatamente um entre período (AAAA-MM) e versão (mcp-api-rag.md).</summary>
public sealed record ConsultaBase(string Consulta, string CorrelacaoId, string? Periodo = null, string? Versao = null, int? TopK = null);

/// <summary>Trecho recuperado. O texto é dado não confiável (P-14).</summary>
/// <param name="Similaridade">Cosseno entre a consulta e o trecho, de 0 a 1, vindo da Api.Rag (research R-38).</param>
public sealed record TrechoBase(
    string TrechoId,
    string Texto,
    ClassificacaoFonte Classificacao,
    string Documento,
    string Versao,
    string Localizacao,
    double? Similaridade = null)
{
    public ReferenciaTrecho ComoReferencia() => new(TrechoId, Documento, Versao, Localizacao, Classificacao);
}

public sealed record ResultadoBusca(IReadOnlyList<TrechoBase> Trechos, bool SemResultados);

/// <summary>A base não respondeu (rede, timeout, autenticação ou erro do servidor).</summary>
public sealed class BaseIndisponivelException(string mensagem, Exception? interna = null) : Exception(mensagem, interna);

/// <summary>A base recusou os argumentos da consulta. A mensagem é estável e pode ser devolvida ao modelo.</summary>
public sealed class ConsultaInvalidaException(string mensagem) : Exception(mensagem);

/// <summary>Cliente da Api.Rag. Todo trecho devolvido é guardado no contexto da execução (P-13).</summary>
public interface IBaseInformacao
{
    Task<ResultadoBusca> BuscarAsync(ConsultaBase consulta, ContextoRecuperacao contexto, CancellationToken cancelamento);

    /// <summary>Nulo quando o trecho não existe ou o documento está inativo.</summary>
    Task<TrechoBase?> ObterTrechoAsync(string trechoId, string correlacaoId, ContextoRecuperacao contexto, CancellationToken cancelamento);
}

/// <summary>Trechos recuperados em uma execução ou mensagem. Só eles podem ser citados pelo LLM (SC-007).</summary>
public sealed class ContextoRecuperacao
{
    private readonly ConcurrentDictionary<string, TrechoBase> _trechos = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<TrechoBase> Trechos => [.. _trechos.Values];

    public void Registrar(IEnumerable<TrechoBase> trechos)
    {
        foreach (var trecho in trechos)
        {
            _trechos[trecho.TrechoId] = trecho;
        }
    }

    public bool Contem(string trechoId) => _trechos.ContainsKey(trechoId);

    public TrechoBase? Obter(string trechoId) => _trechos.GetValueOrDefault(trechoId);
}

public enum TipoSessao
{
    Validacao,
    Conversa,
}

public sealed record ChamadaFerramenta(string Ferramenta, bool Autorizada, string? Motivo, long DuracaoMs);

/// <summary>
/// Contexto de ferramentas de uma execução ou mensagem: quem pede, o que pode ser lido e o registro das
/// chamadas. A autorização é decidida pelo filtro do sistema, não pelo modelo (P-15, research R-11).
/// </summary>
public sealed class SessaoFerramentas(
    TipoSessao tipo,
    Guid idConsumo,
    string orgId,
    string correlacaoId,
    IReadOnlyCollection<string> papeis,
    ContextoRecuperacao recuperacao,
    IReadOnlyList<Achado> achados,
    string periodo,
    bool temExecucao)
{
    /// <summary>Papel usado nas execuções em segundo plano; a autorização do usuário foi verificada no envio.</summary>
    public const string PapelSistema = "sistema";

    private readonly List<ChamadaFerramenta> _chamadas = [];
    private int _chamadasIniciadas;

    public TipoSessao Tipo { get; } = tipo;

    /// <summary>Execução ou conversa à qual o consumo de IA é atribuído (P-17).</summary>
    public Guid IdConsumo { get; } = idConsumo;

    public string OrgId { get; } = orgId;
    public string CorrelacaoId { get; } = correlacaoId;
    public IReadOnlyCollection<string> Papeis { get; } = papeis;
    public ContextoRecuperacao Recuperacao { get; } = recuperacao;
    public IReadOnlyList<Achado> Achados { get; } = achados;
    public string Periodo { get; } = periodo;
    public bool TemExecucao { get; } = temExecucao;

    public IReadOnlyList<ChamadaFerramenta> Chamadas
    {
        get
        {
            lock (_chamadas)
            {
                return [.. _chamadas];
            }
        }
    }

    /// <summary>Reserva uma chamada; falso quando o limite por execução foi atingido.</summary>
    public bool ReservarChamada(int limite) => Interlocked.Increment(ref _chamadasIniciadas) <= limite;

    public void Registrar(ChamadaFerramenta chamada)
    {
        lock (_chamadas)
        {
            _chamadas.Add(chamada);
        }
    }
}

public enum FormatoResposta
{
    Analise,
    Conversa,
}

public sealed record MensagemModelo(bool DoUsuario, string Texto);

/// <param name="Tentativa">1 = original; maior que 1 = retentativa de resposta inválida (P-17).</param>
public sealed record PedidoModelo(
    string InstrucoesSistema,
    IReadOnlyList<MensagemModelo> Mensagens,
    FormatoResposta Formato,
    SessaoFerramentas Sessao,
    int Tentativa);

public sealed record RespostaModelo(string Conteudo);

/// <summary>O provedor do LLM não respondeu. A validação determinística segue sem a análise (FR-084).</summary>
public sealed class ModeloIndisponivelException(string mensagem, Exception? interna = null) : Exception(mensagem, interna);

/// <summary>LLM com ferramentas governadas e saída estruturada estrita. Registra o consumo de cada chamada.</summary>
public interface IModeloAnalise
{
    /// <summary>Identificador do modelo, preenchido pelo sistema no resultado (P-12).</summary>
    string Modelo { get; }

    Task<RespostaModelo> GerarAsync(PedidoModelo pedido, CancellationToken cancelamento);
}

/// <param name="RetentativasResposta">Retentativas após resposta inválida (research R-09).</param>
/// <param name="ConsultasPorExecucao">Buscas da etapa 5, uma por grupo de achados.</param>
/// <param name="MensagensEnviadasAoModelo">Limite do histórico enviado ao modelo, incluindo a pergunta atual.</param>
public sealed record ParametrosAnalise(
    int RetentativasResposta,
    int ConsultasPorExecucao,
    int MensagensEnviadasAoModelo,
    int? TopK = null);
