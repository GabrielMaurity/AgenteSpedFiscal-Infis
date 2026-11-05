using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Estrutural;

/// <summary>Registro pai presente e ocorrência única por pai (FR-012).</summary>
public sealed class ValidadorHierarquia : IValidadorEstrutural
{
    private const int NiveisMaximos = 8;

    private readonly string?[] _caminho = new string?[NiveisMaximos];
    private readonly Dictionary<string, int>[] _filhos =
        Enumerable.Range(0, NiveisMaximos).Select(_ => new Dictionary<string, int>()).ToArray();

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        if (definicao is null || definicao.Nivel >= NiveisMaximos)
        {
            return;
        }

        var nivel = definicao.Nivel;
        if (definicao.Pai is { } pai && _caminho[nivel - 1] != pai)
        {
            contexto.Emitir(
                RegrasEstruturais.Hierarquia,
                $"O registro {definicao.Codigo} exige o registro pai {pai}.",
                linha.Numero,
                definicao.Codigo,
                encontrado: _caminho[nivel - 1],
                esperado: pai);
        }

        var ocorrencias = _filhos[nivel].GetValueOrDefault(definicao.Codigo) + 1;
        _filhos[nivel][definicao.Codigo] = ocorrencias;
        if (definicao.OcorrenciaUnica && ocorrencias > 1)
        {
            contexto.Emitir(
                RegrasEstruturais.OcorrenciaUnica,
                $"O registro {definicao.Codigo} se repete para o mesmo registro pai; o leiaute permite um só.",
                linha.Numero,
                definicao.Codigo);
        }

        _caminho[nivel] = definicao.Codigo;
        for (var i = nivel + 1; i < NiveisMaximos; i++)
        {
            _caminho[i] = null;
            _filhos[i].Clear();
        }
    }

    public void Finalizar(ContextoValidacao contexto)
    {
    }
}
