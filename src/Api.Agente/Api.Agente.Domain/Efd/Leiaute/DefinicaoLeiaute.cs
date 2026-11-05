using Api.Agente.Domain.Achados;

namespace Api.Agente.Domain.Efd.Leiaute;

public sealed record DefinicaoCampo(
    int Numero,
    string Nome,
    string Tipo,
    int? Tamanho,
    bool TamanhoExato,
    int? Decimais,
    string Obrigatorio)
{
    public bool EhNumerico => Tipo == "N";

    public bool EhObrigatorio => Obrigatorio == "O";
}

/// <summary>Situação do documento (COD_SIT) em que o Guia exige só parte dos campos do registro.</summary>
public sealed record ExcecaoObrigatoriedade(
    IReadOnlyList<string> Situacoes,
    IReadOnlyList<string> CamposObrigatorios,
    Fonte Fonte);

public sealed record DefinicaoRegistro(
    string Codigo,
    string Bloco,
    int Nivel,
    string? Pai,
    string Ocorrencia,
    IReadOnlyList<DefinicaoCampo> Campos,
    Fonte Fonte,
    IReadOnlyList<ExcecaoObrigatoriedade>? ExcecoesObrigatoriedade = null)
{
    private const string CampoSituacao = "COD_SIT";

    public bool OcorrenciaUnica => Ocorrencia == "1:1";

    /// <summary>Campos exigidos na situação informada na linha; nulo quando vale a coluna de obrigatoriedade.</summary>
    public IReadOnlyList<string>? CamposExigidosNaSituacao(string[] valores)
    {
        if (ExcecoesObrigatoriedade is null)
        {
            return null;
        }

        var campo = Campos.FirstOrDefault(c => c.Nome == CampoSituacao);
        if (campo is null || campo.Numero > valores.Length)
        {
            return null;
        }

        var situacao = valores[campo.Numero - 1];
        return ExcecoesObrigatoriedade.FirstOrDefault(e => e.Situacoes.Contains(situacao))?.CamposObrigatorios;
    }
}

public sealed record DefinicaoBloco(string Codigo, int Ordem, string Abertura, string Encerramento);

public sealed record VigenciaLeiaute(DateOnly Inicio, DateOnly Fim);

/// <summary>Leiaute de uma versão da EFD, carregado de dados versionados com fonte (P-01).</summary>
public sealed record DefinicaoLeiaute(
    string CodVer,
    string Versao,
    VigenciaLeiaute Vigencia,
    string Codificacao,
    Fonte FonteVersao,
    IReadOnlyList<DefinicaoBloco> Blocos,
    IReadOnlyList<DefinicaoRegistro> Registros)
{
    private readonly Dictionary<string, DefinicaoRegistro> _porCodigo =
        Registros.ToDictionary(r => r.Codigo, StringComparer.Ordinal);

    public DefinicaoRegistro? ObterRegistro(string codigo) => _porCodigo.GetValueOrDefault(codigo);
}
