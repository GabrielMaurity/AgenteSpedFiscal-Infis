using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Analise;

namespace Api.Agente.Domain.Execucoes;

public enum EstadoExecucao
{
    Recebida,
    ValidandoArquivo,
    Identificando,
    Validando,
    ConsultandoBase,
    AnalisandoIa,
    ValidandoSaida,
    Concluida,
    ConcluidaComLimitacoes,
    Falha,
}

public enum StatusEtapa
{
    Pendente,
    Executando,
    Concluida,
    Falha,
    NaoExecutada,
}

public enum CanalExecucao
{
    Rest,
    A2a,
}

public sealed record Etapa(int Numero, string Nome, StatusEtapa Status, string? Motivo, DateTimeOffset? Inicio, DateTimeOffset? Fim);

public sealed record ValidacaoNaoRealizada(string Referencia, Categoria Categoria, string Motivo);

public sealed record ArquivoExecucao(string Nome, long TamanhoBytes, string Sha256);

public sealed record IdentificacaoArquivo(
    string CodVer,
    DateOnly PeriodoInicio,
    DateOnly PeriodoFim,
    string CodFin,
    string Uf,
    string Cnpj,
    string IndPerfil);

/// <summary>Estado completo de uma execução, para persistência. Nunca contém o conteúdo do arquivo (FR-082).</summary>
public sealed record ExecucaoEstado(
    Guid Id,
    string OrgId,
    string UsuarioId,
    string CorrelacaoId,
    CanalExecucao Canal,
    EstadoExecucao Estado,
    List<Etapa> Etapas,
    ArquivoExecucao? Arquivo,
    IdentificacaoArquivo? Identificacao,
    List<string> RegrasExecutadas,
    List<ValidacaoNaoRealizada> ValidacoesNaoRealizadas,
    List<Achado> Achados,
    Dictionary<string, long> TotaisPorRegra,
    string? MotivoFalha,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    DateTimeOffset ExpiraEm,
    List<ValidacaoNaoRealizada>? ForaDoAlcance = null,
    AnaliseIa? AnaliseIa = null);

/// <summary>Processamento de um arquivo, com as nove etapas da constitution P-05.</summary>
public sealed class Execucao
{
    public const int DiasRetencao = 90;

    private static readonly string[] NomesEtapas =
    [
        "Receber arquivo e identificar execução",
        "Validar tamanho, formato e condições",
        "Identificar tipo de documento e versão",
        "Validar estrutura, registros e regras",
        "Consultar a base de informação",
        "Executar ferramentas autorizadas",
        "Produzir análise com o LLM",
        "Validar a saída estruturada",
        "Retornar o resultado",
    ];

    private static readonly EstadoExecucao[] SequenciaEstados =
    [
        EstadoExecucao.Recebida,
        EstadoExecucao.ValidandoArquivo,
        EstadoExecucao.Identificando,
        EstadoExecucao.Validando,
        EstadoExecucao.ConsultandoBase,
        EstadoExecucao.AnalisandoIa,
        EstadoExecucao.ValidandoSaida,
    ];

    private readonly ExecucaoEstado _estado;

    private Execucao(ExecucaoEstado estado) => _estado = estado;

    public Guid Id => _estado.Id;
    public string OrgId => _estado.OrgId;
    public string UsuarioId => _estado.UsuarioId;
    public string CorrelacaoId => _estado.CorrelacaoId;
    public CanalExecucao Canal => _estado.Canal;
    public DateTimeOffset CriadoEm => _estado.CriadoEm;
    public DateTimeOffset AtualizadoEm => _estado.AtualizadoEm;
    public ArquivoExecucao? Arquivo { get; private set; }
    public IdentificacaoArquivo? Identificacao { get; private set; }
    public IReadOnlyList<string> RegrasExecutadas => _estado.RegrasExecutadas;
    public EstadoExecucao Estado { get; private set; }
    public DateTimeOffset ExpiraEm => _estado.ExpiraEm;
    public IReadOnlyList<Etapa> Etapas => _estado.Etapas;
    public IReadOnlyList<Achado> Achados => _estado.Achados;
    public IReadOnlyList<ValidacaoNaoRealizada> ValidacoesNaoRealizadas => _estado.ValidacoesNaoRealizadas;

    /// <summary>Limites permanentes da validação: sempre listados, sem efeito no status nem na revisão (research R-30).</summary>
    public IReadOnlyList<ValidacaoNaoRealizada> ForaDoAlcance => _estado.ForaDoAlcance!;
    public IReadOnlyDictionary<string, long> TotaisPorRegra => _estado.TotaisPorRegra;
    public string? MotivoFalha { get; private set; }

    /// <summary>Resultado das etapas 5 a 8, separado dos achados determinísticos (FR-047).</summary>
    public AnaliseIa? AnaliseIa { get; private set; }

    public bool EstaFinalizada => Estado is EstadoExecucao.Concluida or EstadoExecucao.ConcluidaComLimitacoes or EstadoExecucao.Falha;

    /// <summary>Revisão humana conforme FR-046. Uma execução com falha também exige revisão.</summary>
    public bool RevisaoHumanaNecessaria =>
        Estado == EstadoExecucao.Falha
        || _estado.ValidacoesNaoRealizadas.Count > 0
        || _estado.Achados.Any(a =>
            a.Severidade is Severidade.Impeditiva or Severidade.Advertencia
            || a.Categoria is Categoria.IndicioIa or Categoria.AusenciaEvidencias);

    public static Execucao Criar(Guid id, string orgId, string usuarioId, string correlacaoId, CanalExecucao canal, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orgId);
        ArgumentException.ThrowIfNullOrWhiteSpace(usuarioId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlacaoId);

        var etapas = NomesEtapas
            .Select((nome, indice) => new Etapa(indice + 1, nome, StatusEtapa.Pendente, null, null, null))
            .ToList();

        return new Execucao(new ExecucaoEstado(
            id, orgId, usuarioId, correlacaoId, canal, EstadoExecucao.Recebida, etapas, null, null,
            [], [], [], [], null, agora, agora, agora.AddDays(DiasRetencao), []))
        {
            Estado = EstadoExecucao.Recebida,
        };
    }

    /// <summary>Execuções gravadas antes da lista de itens fora do alcance a restauram vazia.</summary>
    public static Execucao Restaurar(ExecucaoEstado estado) =>
        new(estado with { ForaDoAlcance = estado.ForaDoAlcance ?? [] })
        {
            Estado = estado.Estado,
            MotivoFalha = estado.MotivoFalha,
            Arquivo = estado.Arquivo,
            Identificacao = estado.Identificacao,
            AnaliseIa = estado.AnaliseIa,
        };

    public ExecucaoEstado Exportar(DateTimeOffset agora) =>
        _estado with
        {
            Estado = Estado,
            MotivoFalha = MotivoFalha,
            Arquivo = Arquivo,
            Identificacao = Identificacao,
            AnaliseIa = AnaliseIa,
            AtualizadoEm = agora,
        };

    public void DefinirArquivo(ArquivoExecucao arquivo)
    {
        GarantirNaoFinalizada();
        Arquivo = arquivo;
    }

    public void DefinirIdentificacao(IdentificacaoArquivo identificacao)
    {
        GarantirNaoFinalizada();
        Identificacao = identificacao;
    }

    /// <summary>Registra o resultado da validação determinística, com os totais reais por regra.</summary>
    public void AplicarResultado(
        IEnumerable<Achado> achados,
        IReadOnlyDictionary<string, long> totais,
        IEnumerable<string> regrasExecutadas,
        IEnumerable<ValidacaoNaoRealizada> naoRealizadas,
        IEnumerable<ValidacaoNaoRealizada> foraDoAlcance)
    {
        GarantirNaoFinalizada();
        _estado.Achados.AddRange(achados);
        _estado.ForaDoAlcance!.AddRange(foraDoAlcance);
        foreach (var (regra, total) in totais)
        {
            _estado.TotaisPorRegra[regra] = _estado.TotaisPorRegra.GetValueOrDefault(regra) + total;
        }

        _estado.RegrasExecutadas.AddRange(regrasExecutadas);
        _estado.ValidacoesNaoRealizadas.AddRange(naoRealizadas);
    }

    public void AvancarPara(EstadoExecucao proximo)
    {
        GarantirNaoFinalizada();

        var atual = Array.IndexOf(SequenciaEstados, Estado);
        var alvo = Array.IndexOf(SequenciaEstados, proximo);
        if (alvo != atual + 1)
        {
            throw new InvalidOperationException($"Transição inválida: {Estado} para {proximo}.");
        }

        Estado = proximo;
    }

    public void AtualizarEtapa(int numero, StatusEtapa status, DateTimeOffset agora, string? motivo = null)
    {
        var indice = numero - 1;
        if (indice < 0 || indice >= _estado.Etapas.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(numero));
        }

        var etapa = _estado.Etapas[indice];
        _estado.Etapas[indice] = etapa with
        {
            Status = status,
            Motivo = motivo,
            Inicio = etapa.Inicio ?? (status == StatusEtapa.NaoExecutada ? null : agora),
            Fim = status == StatusEtapa.Executando ? null : agora,
        };
    }

    public void RegistrarAchado(Achado achado)
    {
        GarantirNaoFinalizada();
        _estado.Achados.Add(achado);
    }

    public void RegistrarOcorrencia(string regraId)
    {
        GarantirNaoFinalizada();
        _estado.TotaisPorRegra[regraId] = _estado.TotaisPorRegra.GetValueOrDefault(regraId) + 1;
    }

    public void RegistrarValidacaoNaoRealizada(ValidacaoNaoRealizada validacao)
    {
        GarantirNaoFinalizada();
        _estado.ValidacoesNaoRealizadas.Add(validacao);
    }

    /// <summary>
    /// Registra a análise da IA. Os indícios aceitos entram como achados <c>indicio_ia</c> de origem IA,
    /// ao lado dos determinísticos e sem alterá-los (FR-047, research R-10).
    /// </summary>
    public void RegistrarAnaliseIa(AnaliseIa analise)
    {
        ArgumentNullException.ThrowIfNull(analise);
        GarantirNaoFinalizada();
        if (AnaliseIa is not null)
        {
            throw new InvalidOperationException("A análise da IA já foi registrada nesta execução.");
        }

        AnaliseIa = analise;
        if (analise.Status != StatusAnaliseIa.Aceita || analise.Conteudo is null)
        {
            return;
        }

        var numero = 0;
        foreach (var indicio in analise.Conteudo.Indicios)
        {
            numero++;
            _estado.Achados.Add(new Achado(
                $"IA-{numero:D3}",
                Categoria.IndicioIa,
                indicio.Severidade,
                Origem.Ia,
                indicio.Descricao,
                registro: indicio.Registro));
        }
    }

    /// <summary>Encerra a execução. Só fica concluída sem pendências e com a análise da IA aceita (P-04).</summary>
    public void Concluir(bool analiseIaAceita)
    {
        GarantirNaoFinalizada();
        Estado = analiseIaAceita && _estado.ValidacoesNaoRealizadas.Count == 0
            ? EstadoExecucao.Concluida
            : EstadoExecucao.ConcluidaComLimitacoes;
    }

    public void Falhar(string motivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        GarantirNaoFinalizada();
        Estado = EstadoExecucao.Falha;
        MotivoFalha = motivo;
    }

    private void GarantirNaoFinalizada()
    {
        if (EstaFinalizada)
        {
            throw new InvalidOperationException($"A execução {Id} já foi finalizada.");
        }
    }
}
