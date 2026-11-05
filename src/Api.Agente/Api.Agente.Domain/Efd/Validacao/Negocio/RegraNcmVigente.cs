using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Negocio;

/// <summary>
/// O NCM de cada item existe e está vigente na tabela importada (FR-022). Sem tabela para o período, a
/// regra não roda e o motor a lista como não realizada. O campo vazio não é tratado aqui: o Guia o
/// exige só em alguns casos.
/// </summary>
public sealed class RegraNcmVigente(ITabelaNcm tabela) : IValidadorEstrutural
{
    private const string CampoNcm = "COD_NCM";

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        if (definicao is not { Codigo: "0200" } || !definicao.TemQuantidadeCorreta(campos))
        {
            return;
        }

        var ncm = definicao.Valor(campos, CampoNcm);
        if (ncm.Length == 0)
        {
            return;
        }

        var situacao = tabela.Consultar(ncm);
        if (situacao == SituacaoNcm.Vigente)
        {
            return;
        }

        var problema = situacao == SituacaoNcm.NaoVigente ? "não está vigente no período do arquivo" : "não existe";
        contexto.Emitir(
            RegrasNegocio.NcmVigente,
            $"O NCM {ncm} do item {definicao.Valor(campos, "COD_ITEM")} {problema} na tabela oficial importada, versão {tabela.Versao}.",
            linha.Numero,
            definicao.Codigo,
            CampoNcm,
            ncm);
    }

    public void Finalizar(ContextoValidacao contexto)
    {
    }
}
