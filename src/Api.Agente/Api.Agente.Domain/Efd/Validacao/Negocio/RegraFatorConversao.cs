using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Negocio;

/// <summary>
/// Unidade movimentada diferente da unidade de estoque do 0200 exige 0220 para o item (FR-021).
/// Vale só para os registros em que o Guia faz essa exigência. Material de uso e consumo fica fora em
/// todos: o Guia o dispensa no C170, e a exceção evita falso erro nos demais, que não tratam do caso.
/// </summary>
public sealed class RegraFatorConversao : IValidadorEstrutural
{
    private const string TipoUsoConsumo = "07";
    private const string CampoItem = "COD_ITEM";
    private const string CampoUnidade = "UNID";

    /// <summary>C180 a C880 não têm COD_ITEM próprio: o item é o do registro pai, a linha de item anterior.</summary>
    private static readonly HashSet<string> RegistrosExigidos = new(StringComparer.Ordinal)
    {
        "C170", "C180", "C181", "C185", "C330", "C380", "C425", "C430", "C480", "C815", "C870", "C880", "G140",
    };

    private readonly Dictionary<string, ItemCadastrado> _itens = new(StringComparer.Ordinal);
    private ItemCadastrado? _ultimoCadastrado;
    private string _ultimoItem = "";

    private sealed record ItemCadastrado(string UnidadeEstoque, string Tipo, HashSet<string> Conversoes);

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        if (campos[0] == "0220")
        {
            if (campos.Length > 1)
            {
                _ultimoCadastrado?.Conversoes.Add(campos[1]);
            }

            return;
        }

        if (definicao is null || !definicao.TemQuantidadeCorreta(campos))
        {
            return;
        }

        if (definicao.TemCampo(CampoItem))
        {
            _ultimoItem = definicao.Valor(campos, CampoItem);
        }

        if (definicao.Codigo == "0200")
        {
            _ultimoCadastrado = new ItemCadastrado(
                definicao.Valor(campos, "UNID_INV"),
                definicao.Valor(campos, "TIPO_ITEM"),
                new HashSet<string>(StringComparer.Ordinal));
            _itens[_ultimoItem] = _ultimoCadastrado;
            return;
        }

        if (RegistrosExigidos.Contains(definicao.Codigo))
        {
            Conferir(linha.Numero, definicao.Codigo, definicao.Valor(campos, CampoUnidade), contexto);
        }
    }

    public void Finalizar(ContextoValidacao contexto)
    {
    }

    private void Conferir(long linha, string registro, string unidade, ContextoValidacao contexto)
    {
        // Item fora do 0200 não tem unidade de estoque com que comparar.
        if (unidade.Length == 0
            || !_itens.TryGetValue(_ultimoItem, out var item)
            || item.Tipo == TipoUsoConsumo
            || unidade == item.UnidadeEstoque
            || item.Conversoes.Contains(unidade))
        {
            return;
        }

        contexto.Emitir(
            RegrasNegocio.FatorConversao,
            $"O item {_ultimoItem} foi movimentado na unidade {unidade}, mas a unidade de estoque é {item.UnidadeEstoque} e não há registro 0220 para essa conversão.",
            linha,
            registro,
            CampoUnidade,
            unidade,
            item.UnidadeEstoque);
    }
}
