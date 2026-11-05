namespace Api.Agente.Domain.Efd.Validacao.Negocio;

public enum SituacaoNcm
{
    Vigente,
    NaoVigente,
    Inexistente,
}

/// <summary>Tabela oficial carregada para a execução, sem consulta externa durante a validação (FR-022).</summary>
public interface ITabelaNcm
{
    string Versao { get; }

    SituacaoNcm Consultar(string codigo);
}

public sealed record CodigoNcm(string Codigo, DateOnly? VigenciaInicio, DateOnly? VigenciaFim);

/// <summary>Códigos de uma versão, avaliados no período do arquivo: vale o código vigente em parte dele.</summary>
public sealed class TabelaNcm : ITabelaNcm
{
    private readonly Dictionary<string, SituacaoNcm> _situacoes = new(StringComparer.Ordinal);

    public TabelaNcm(string versao, IEnumerable<CodigoNcm> codigos, DateOnly periodoInicio, DateOnly periodoFim)
    {
        Versao = versao;
        foreach (var codigo in codigos)
        {
            var vigente = (codigo.VigenciaInicio is null || codigo.VigenciaInicio <= periodoFim)
                && (codigo.VigenciaFim is null || codigo.VigenciaFim >= periodoInicio);

            // O mesmo código pode ter mais de um intervalo: basta um cobrir o período.
            if (vigente || !_situacoes.ContainsKey(codigo.Codigo))
            {
                _situacoes[codigo.Codigo] = vigente ? SituacaoNcm.Vigente : SituacaoNcm.NaoVigente;
            }
        }
    }

    public string Versao { get; }

    public SituacaoNcm Consultar(string codigo) => _situacoes.GetValueOrDefault(codigo, SituacaoNcm.Inexistente);
}
