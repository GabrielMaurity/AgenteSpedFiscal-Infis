namespace Api.Agente.Domain.Achados;

public enum Categoria
{
    ErroEstrutural,
    InconsistenciaNegocio,
    IndicioIa,
    AusenciaEvidencias,
    FalhaTecnica,
}

public enum Severidade
{
    Impeditiva,
    Advertencia,
    Informativa,
}

public enum Origem
{
    Validador,
    Ia,
}

/// <summary>Referência a um documento oficial. Documento, versão e localização são obrigatórios (P-01).</summary>
public sealed record Fonte
{
    public Fonte(string documento, string versao, string localizacao, Guid? documentoId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documento);
        ArgumentException.ThrowIfNullOrWhiteSpace(versao);
        ArgumentException.ThrowIfNullOrWhiteSpace(localizacao);
        Documento = documento;
        Versao = versao;
        Localizacao = localizacao;
        DocumentoId = documentoId;
    }

    public string Documento { get; }
    public string Versao { get; }
    public string Localizacao { get; }
    public Guid? DocumentoId { get; }
}

/// <summary>Resultado de uma regra ou análise, em exatamente uma categoria (P-03).</summary>
public sealed class Achado
{
    public Achado(
        string id,
        Categoria categoria,
        Severidade severidade,
        Origem origem,
        string mensagem,
        string? regraId = null,
        long? linha = null,
        string? registro = null,
        string? campo = null,
        string? valorEncontrado = null,
        string? valorEsperado = null,
        string? acaoCorretiva = null,
        Fonte? fonte = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(mensagem);

        if (categoria == Categoria.IndicioIa)
        {
            if (severidade == Severidade.Impeditiva)
            {
                throw new ArgumentException("Indício da IA nunca é impeditivo.", nameof(severidade));
            }

            if (origem != Origem.Ia)
            {
                throw new ArgumentException("Indício da IA exige origem IA.", nameof(origem));
            }
        }

        if (categoria is Categoria.ErroEstrutural or Categoria.InconsistenciaNegocio)
        {
            if (origem != Origem.Validador)
            {
                throw new ArgumentException("Erro estrutural e inconsistência de negócio exigem origem validador.", nameof(origem));
            }

            if (string.IsNullOrWhiteSpace(regraId))
            {
                throw new ArgumentException("Achado determinístico exige regra do catálogo.", nameof(regraId));
            }

            if (fonte is null)
            {
                throw new ArgumentException("Achado determinístico exige fonte oficial.", nameof(fonte));
            }
        }

        Id = id;
        Categoria = categoria;
        Severidade = severidade;
        Origem = origem;
        Mensagem = mensagem;
        RegraId = regraId;
        Linha = linha;
        Registro = registro;
        Campo = campo;
        ValorEncontrado = valorEncontrado;
        ValorEsperado = valorEsperado;
        AcaoCorretiva = acaoCorretiva;
        Fonte = fonte;
    }

    public string Id { get; }
    public Categoria Categoria { get; }
    public Severidade Severidade { get; }
    public Origem Origem { get; }
    public string Mensagem { get; }
    public string? RegraId { get; }
    public long? Linha { get; }
    public string? Registro { get; }
    public string? Campo { get; }
    public string? ValorEncontrado { get; }
    public string? ValorEsperado { get; }
    public string? AcaoCorretiva { get; }
    public Fonte? Fonte { get; }
}
