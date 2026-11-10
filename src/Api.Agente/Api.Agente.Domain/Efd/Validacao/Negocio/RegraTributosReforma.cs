using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Negocio;

/// <summary>
/// CBS, IBS e IS não entram no VL_OPR dos registros analíticos (Guia, Cap. I, Seção 10; FR-025). Esses
/// valores não constam do arquivo, só do XML, então a verificação nunca é concluída aqui: havendo registro
/// analítico, fica na lista de itens fora do alcance (research R-30). Em 2026, a inclusão no VL_DOC não é exigida.
/// </summary>
public sealed class RegraTributosReforma : IValidadorEstrutural
{
    private const string Motivo =
        "CBS, IBS e IS só constam do XML do documento fiscal: não foi possível verificar se ficaram fora do VL_OPR dos registros analíticos.";

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        if (definicao is not null && definicao.TemCampo("VL_OPR"))
        {
            contexto.RegistrarForaDoAlcance(RegrasNegocio.TributosReforma, Motivo);
        }
    }

    public void Finalizar(ContextoValidacao contexto)
    {
    }
}
