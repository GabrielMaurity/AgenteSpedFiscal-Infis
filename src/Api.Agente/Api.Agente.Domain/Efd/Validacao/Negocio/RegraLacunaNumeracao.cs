using System.Globalization;
using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Negocio;

/// <summary>
/// Lacuna na numeração de documentos próprios, por modelo e série (FR-024). Todo número informado conta,
/// inclusive cancelado, denegado ou inutilizado. Só o C100 é verificado, e documentos de terceiros ficam
/// de fora. A regra só executa quando ativa no catálogo, o que hoje não ocorre: não há fonte oficial.
/// </summary>
public sealed class RegraLacunaNumeracao : IValidadorEstrutural
{
    private readonly Dictionary<(string Modelo, string Serie), List<long>> _numeros = [];

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        if (definicao is not { Codigo: "C100" }
            || !definicao.TemQuantidadeCorreta(campos)
            || definicao.Valor(campos, "IND_EMIT") != "0"
            || !long.TryParse(definicao.Valor(campos, "NUM_DOC"), NumberStyles.None, CultureInfo.InvariantCulture, out var numero))
        {
            return;
        }

        var chave = (definicao.Valor(campos, "COD_MOD"), definicao.Valor(campos, "SER"));
        if (!_numeros.TryGetValue(chave, out var numeros))
        {
            numeros = [];
            _numeros[chave] = numeros;
        }

        numeros.Add(numero);
    }

    public void Finalizar(ContextoValidacao contexto)
    {
        foreach (var ((modelo, serie), numeros) in _numeros)
        {
            numeros.Sort();
            for (var i = 1; i < numeros.Count; i++)
            {
                if (numeros[i] - numeros[i - 1] <= 1)
                {
                    continue;
                }

                var inicio = (numeros[i - 1] + 1).ToString(CultureInfo.InvariantCulture);
                var fim = (numeros[i] - 1).ToString(CultureInfo.InvariantCulture);
                var intervalo = inicio == fim ? inicio : $"{inicio} a {fim}";
                contexto.Emitir(
                    RegrasNegocio.LacunaNumeracao,
                    $"Modelo {modelo}, série {serie}: faltam no arquivo os documentos de emissão própria de número {intervalo}.",
                    registro: "C100",
                    campo: "NUM_DOC",
                    encontrado: intervalo);
            }
        }
    }
}
