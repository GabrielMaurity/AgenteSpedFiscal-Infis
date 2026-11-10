namespace Api.Rag.Domain.Documentos;

public enum ClassificacaoDocumento
{
    Oficial,
    Interno,
}

public enum StatusDocumento
{
    Recebido,
    Processando,
    Ativo,
    Falha,
    Inativo,
}

public sealed class DocumentoInvalidoException(string mensagem) : Exception(mensagem);

public sealed class TransicaoInvalidaException(string mensagem) : Exception(mensagem);

/// <summary>Tipos aceitos na ingestão e a classificação que cada um exige (FR-060, research R-27).</summary>
public static class TiposDocumento
{
    public const string GuiaPratico = "guia_pratico_efd_icms_ipi";
    public const string MaterialInterno = "material_interno";

    public static readonly IReadOnlySet<string> Oficiais = new HashSet<string>(StringComparer.Ordinal)
    {
        GuiaPratico, "nota_tecnica_leiaute", "ato_cotepe", "legislacao", "outro_oficial",
    };

    public static bool Existe(string tipo) => Oficiais.Contains(tipo) || tipo == MaterialInterno;

    public static bool Compativel(string tipo, ClassificacaoDocumento classificacao) =>
        classificacao == ClassificacaoDocumento.Interno ? tipo == MaterialInterno : Oficiais.Contains(tipo);
}

/// <summary>
/// Documento da base de informação. A classificação é imutável depois da ingestão (P-09). Estados:
/// recebido → processando → ativo | falha; ativo ou falha → processando (reprocessar); ativo ou falha → inativo.
/// </summary>
public sealed class Documento
{
    private Documento(
        Guid id,
        ClassificacaoDocumento classificacao,
        string tipoDocumento,
        string titulo,
        string origem,
        string versao,
        DateOnly? vigenciaInicio,
        DateOnly? vigenciaFim,
        string sha256,
        DateTimeOffset ingeridoEm,
        string ingeridoPor)
    {
        Id = id;
        Classificacao = classificacao;
        TipoDocumento = tipoDocumento;
        Titulo = titulo;
        Origem = origem;
        Versao = versao;
        VigenciaInicio = vigenciaInicio;
        VigenciaFim = vigenciaFim;
        Sha256 = sha256;
        IngeridoEm = ingeridoEm;
        IngeridoPor = ingeridoPor;
    }

    public Guid Id { get; }
    public ClassificacaoDocumento Classificacao { get; }
    public string TipoDocumento { get; }
    public string Titulo { get; }
    public string Origem { get; }
    public string Versao { get; }
    public DateOnly? VigenciaInicio { get; }
    public DateOnly? VigenciaFim { get; }
    public string Sha256 { get; }
    public DateTimeOffset IngeridoEm { get; }
    public string IngeridoPor { get; }
    public StatusDocumento Status { get; private set; }
    public string? ModeloEmbedding { get; private set; }
    public int? Dimensao { get; private set; }
    public string? MotivoFalha { get; private set; }

    /// <summary>Recusa metadados ausentes e classificação incompatível com o tipo (FR-060).</summary>
    public static Documento Criar(
        Guid id,
        ClassificacaoDocumento classificacao,
        string tipoDocumento,
        string titulo,
        string origem,
        string versao,
        DateOnly? vigenciaInicio,
        DateOnly? vigenciaFim,
        string sha256,
        DateTimeOffset ingeridoEm,
        string ingeridoPor)
    {
        Exigir(tipoDocumento, "tipoDocumento");
        Exigir(titulo, "titulo");
        Exigir(origem, "origem");
        Exigir(versao, "versao");
        Exigir(ingeridoPor, "ingeridoPor");

        if (!TiposDocumento.Existe(tipoDocumento))
        {
            throw new DocumentoInvalidoException($"tipoDocumento desconhecido: {tipoDocumento}.");
        }

        if (!TiposDocumento.Compativel(tipoDocumento, classificacao))
        {
            throw new DocumentoInvalidoException(classificacao == ClassificacaoDocumento.Interno
                ? "Material interno deve usar tipoDocumento material_interno."
                : "Documento oficial não pode usar tipoDocumento material_interno.");
        }

        if (vigenciaFim < vigenciaInicio)
        {
            throw new DocumentoInvalidoException("vigenciaFim é anterior a vigenciaInicio.");
        }

        if (sha256.Length != 64)
        {
            throw new DocumentoInvalidoException("sha256 inválido.");
        }

        return new Documento(id, classificacao, tipoDocumento, titulo, origem, versao, vigenciaInicio, vigenciaFim, sha256, ingeridoEm, ingeridoPor)
        {
            Status = StatusDocumento.Recebido,
        };
    }

    /// <summary>Reconstrói um documento gravado, sem revalidar (o banco já guarda só estados válidos).</summary>
    public static Documento Restaurar(
        Guid id,
        ClassificacaoDocumento classificacao,
        string tipoDocumento,
        string titulo,
        string origem,
        string versao,
        DateOnly? vigenciaInicio,
        DateOnly? vigenciaFim,
        string sha256,
        DateTimeOffset ingeridoEm,
        string ingeridoPor,
        StatusDocumento status,
        string? modeloEmbedding,
        int? dimensao,
        string? motivoFalha) =>
        new(id, classificacao, tipoDocumento, titulo, origem, versao, vigenciaInicio, vigenciaFim, sha256, ingeridoEm, ingeridoPor)
        {
            Status = status,
            ModeloEmbedding = modeloEmbedding,
            Dimensao = dimensao,
            MotivoFalha = motivoFalha,
        };

    /// <summary>Documento vigente em algum dia do intervalo; sem vigência informada, vale para qualquer período.</summary>
    public bool VigenteEm(DateOnly inicio, DateOnly fim) =>
        (VigenciaInicio is null || VigenciaInicio <= fim) && (VigenciaFim is null || VigenciaFim >= inicio);

    public void IniciarProcessamento()
    {
        if (Status is not (StatusDocumento.Recebido or StatusDocumento.Ativo or StatusDocumento.Falha or StatusDocumento.Processando))
        {
            throw new TransicaoInvalidaException($"Documento {Status.ToString().ToLowerInvariant()} não pode ser processado.");
        }

        Status = StatusDocumento.Processando;
        MotivoFalha = null;
    }

    public void Ativar(string modeloEmbedding, int dimensao)
    {
        Garantir(StatusDocumento.Processando, "ativado");
        ArgumentException.ThrowIfNullOrWhiteSpace(modeloEmbedding);
        Status = StatusDocumento.Ativo;
        ModeloEmbedding = modeloEmbedding;
        Dimensao = dimensao;
    }

    public void Falhar(string motivo)
    {
        Garantir(StatusDocumento.Processando, "marcado como falha");
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        Status = StatusDocumento.Falha;
        MotivoFalha = motivo;
    }

    /// <summary>A versão deixa de ser recuperada, mas fica no histórico (FR-061).</summary>
    public void Desativar()
    {
        if (Status is not (StatusDocumento.Ativo or StatusDocumento.Falha))
        {
            throw new TransicaoInvalidaException($"Documento {Status.ToString().ToLowerInvariant()} não pode ser desativado.");
        }

        Status = StatusDocumento.Inativo;
    }

    private void Garantir(StatusDocumento esperado, string acao)
    {
        if (Status != esperado)
        {
            throw new TransicaoInvalidaException($"Documento {Status.ToString().ToLowerInvariant()} não pode ser {acao}.");
        }
    }

    private static void Exigir(string valor, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new DocumentoInvalidoException($"{campo} é obrigatório.");
        }
    }
}
