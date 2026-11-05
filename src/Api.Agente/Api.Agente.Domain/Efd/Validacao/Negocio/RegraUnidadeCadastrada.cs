using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Negocio;

/// <summary>Toda unidade usada em cadastro, documento ou inventário está no 0190 (FR-020).</summary>
public sealed class RegraUnidadeCadastrada : IValidadorEstrutural
{
    private const string RegistroUnidades = "0190";
    private const string EncerramentoBloco0 = "0990";
    private const string CampoItem = "COD_ITEM";

    private static readonly string[] CamposUnidade = ["UNID", "UNID_INV", "UNID_CONV"];

    private readonly HashSet<string> _cadastradas = new(StringComparer.Ordinal);
    private readonly List<UsoUnidade> _antesDoFimDoBloco0 = [];
    private bool _bloco0Encerrado;
    private string _ultimoItem = "";

    private sealed record UsoUnidade(long Linha, string Registro, string Campo, string Unidade, string Item);

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        if (campos[0] == RegistroUnidades)
        {
            // Mesmo com erro na linha, a unidade foi declarada: o erro já tem o seu achado estrutural.
            if (campos.Length > 1)
            {
                _cadastradas.Add(campos[1]);
            }

            return;
        }

        if (campos[0] == EncerramentoBloco0)
        {
            ConferirPendentes(contexto);
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

        foreach (var campo in CamposUnidade)
        {
            var unidade = definicao.Valor(campos, campo);
            if (unidade.Length == 0)
            {
                continue;
            }

            var uso = new UsoUnidade(linha.Numero, definicao.Codigo, campo, unidade, _ultimoItem);
            if (_bloco0Encerrado)
            {
                Conferir(uso, contexto);
            }
            else
            {
                _antesDoFimDoBloco0.Add(uso);
            }
        }
    }

    public void Finalizar(ContextoValidacao contexto) => ConferirPendentes(contexto);

    private void ConferirPendentes(ContextoValidacao contexto)
    {
        _bloco0Encerrado = true;
        foreach (var uso in _antesDoFimDoBloco0)
        {
            Conferir(uso, contexto);
        }

        _antesDoFimDoBloco0.Clear();
    }

    private void Conferir(UsoUnidade uso, ContextoValidacao contexto)
    {
        if (_cadastradas.Contains(uso.Unidade))
        {
            return;
        }

        var item = uso.Item.Length > 0 ? $" do item {uso.Item}" : "";
        contexto.Emitir(
            RegrasNegocio.UnidadeCadastrada,
            $"A unidade {uso.Unidade}{item}, usada no registro {uso.Registro}, não está cadastrada no registro 0190.",
            uso.Linha,
            uso.Registro,
            uso.Campo,
            uso.Unidade);
    }
}
