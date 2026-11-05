using System.Globalization;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Catalogo;
using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Domain.Efd.Validacao;

/// <summary>Coleta achados das regras ativas: guarda até o limite por regra e conta o total real (research R-08).</summary>
public sealed class ContextoValidacao(
    DefinicaoLeiaute leiaute,
    IReadOnlyDictionary<string, RegraCatalogo> regrasAtivas,
    int ocorrenciasDetalhadasPorRegra)
{
    private const int TamanhoMaximoValor = 100;

    private readonly Dictionary<string, long> _totais = [];
    private readonly List<Achado> _achados = [];
    private readonly List<ValidacaoNaoRealizada> _naoRealizadas = [];

    public DefinicaoLeiaute Leiaute => leiaute;

    public IReadOnlyList<Achado> Achados => _achados;

    public IReadOnlyDictionary<string, long> Totais => _totais;

    public IReadOnlyList<ValidacaoNaoRealizada> NaoRealizadas => _naoRealizadas;

    /// <summary>Regra ativa que não pôde verificar o arquivo por falta de dado (FR-044).</summary>
    public void RegistrarNaoRealizada(string regraId, Categoria categoria, string motivo)
    {
        if (regrasAtivas.ContainsKey(regraId) && _naoRealizadas.All(n => n.Referencia != regraId))
        {
            _naoRealizadas.Add(new ValidacaoNaoRealizada(regraId, categoria, motivo));
        }
    }

    public void Emitir(
        string regraId,
        string mensagem,
        long? linha = null,
        string? registro = null,
        string? campo = null,
        string? encontrado = null,
        string? esperado = null)
    {
        if (!regrasAtivas.TryGetValue(regraId, out var regra))
        {
            return;
        }

        var total = _totais.GetValueOrDefault(regraId) + 1;
        _totais[regraId] = total;
        if (total > ocorrenciasDetalhadasPorRegra)
        {
            return;
        }

        _achados.Add(new Achado(
            $"{regraId}-{total.ToString(CultureInfo.InvariantCulture)}",
            regra.Categoria,
            regra.SeveridadeAchado,
            Origem.Validador,
            mensagem,
            regraId,
            linha,
            registro,
            campo,
            Encurtar(encontrado),
            Encurtar(esperado),
            regra.AcaoCorretiva,
            regra.Fonte));
    }

    private static string? Encurtar(string? valor) =>
        valor is { Length: > TamanhoMaximoValor } ? valor[..TamanhoMaximoValor] : valor;
}

/// <summary>Validador em fluxo: recebe cada registro e conclui ao final da passagem.</summary>
public interface IValidadorEstrutural
{
    void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto);

    void Finalizar(ContextoValidacao contexto);
}

public static class RegrasEstruturais
{
    public const string Delimitador = "EFD-ESTR-001";
    public const string FimDeLinha = "EFD-ESTR-002";
    public const string RegistroDesconhecido = "EFD-ESTR-003";
    public const string QuantidadeCampos = "EFD-ESTR-004";
    public const string CampoObrigatorio = "EFD-ESTR-005";
    public const string CampoNumerico = "EFD-ESTR-006";
    public const string CampoAlfanumerico = "EFD-ESTR-007";
    public const string TamanhoCampo = "EFD-ESTR-008";
    public const string CasasDecimais = "EFD-ESTR-009";
    public const string OrdemBlocos = "EFD-ESTR-010";
    public const string AberturaEncerramento = "EFD-ESTR-011";
    public const string Hierarquia = "EFD-ESTR-012";
    public const string OcorrenciaUnica = "EFD-ESTR-013";
    public const string IndicadorMovimento = "EFD-ESTR-014";
    public const string TotalBloco = "EFD-ESTR-015";
    public const string TotalRegistro9900 = "EFD-ESTR-016";
    public const string TotalArquivo = "EFD-ESTR-017";
    public const string InicioEFim = "EFD-ESTR-018";
}
