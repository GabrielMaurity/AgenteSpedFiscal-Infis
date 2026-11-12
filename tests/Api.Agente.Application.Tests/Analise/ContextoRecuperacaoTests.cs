using Api.Agente.Application.Analise;
using Api.Agente.Application.Conversas;
using Api.Agente.Application.Tests.Fakes;

namespace Api.Agente.Application.Tests.Analise;

/// <summary>
/// A similaridade de um trecho é a da busca feita com a pergunta do usuário (research R-38). Reler o trecho
/// por obter_trecho, que não tem consulta, ou achá-lo de novo em outra busca da ferramenta não a substitui.
/// </summary>
public class ContextoRecuperacaoTests
{
    [Fact]
    public void Reler_o_trecho_sem_similaridade_mantem_a_da_busca_original()
    {
        var contexto = new ContextoRecuperacao();
        contexto.Registrar([BaseInformacaoFalsa.TrechoOficial with { Similaridade = 0.57 }]);

        contexto.Registrar([BaseInformacaoFalsa.TrechoOficial with { Similaridade = null }]);

        Assert.Equal(0.57, contexto.Obter(BaseInformacaoFalsa.TrechoOficial.TrechoId)!.Similaridade);
    }

    [Fact]
    public void Outra_busca_nao_troca_a_similaridade_da_primeira()
    {
        var contexto = new ContextoRecuperacao();
        contexto.Registrar([BaseInformacaoFalsa.TrechoOficial with { Similaridade = 0.57 }]);

        contexto.Registrar([BaseInformacaoFalsa.TrechoOficial with { Similaridade = 0.91 }]);

        Assert.Equal(0.57, contexto.Obter(BaseInformacaoFalsa.TrechoOficial.TrechoId)!.Similaridade);
    }

    [Fact]
    public void Trecho_conhecido_primeiro_sem_similaridade_recebe_a_que_chegar_depois()
    {
        var contexto = new ContextoRecuperacao();
        contexto.Registrar([BaseInformacaoFalsa.TrechoOficial with { Similaridade = null }]);

        contexto.Registrar([BaseInformacaoFalsa.TrechoOficial with { Similaridade = 0.4 }]);

        Assert.Equal(0.4, contexto.Obter(BaseInformacaoFalsa.TrechoOficial.TrechoId)!.Similaridade);
    }

    [Fact]
    public void Proximidade_usa_a_similaridade_preservada_dos_trechos_citados()
    {
        var contexto = new ContextoRecuperacao();
        contexto.Registrar([BaseInformacaoFalsa.TrechoOficial with { Similaridade = 0.57 }]);
        contexto.Registrar([BaseInformacaoFalsa.TrechoOficial with { Similaridade = null }]);

        var proximidade = ConversasHandler.Proximidade([BaseInformacaoFalsa.TrechoOficial.ComoReferencia()], contexto);

        Assert.Equal(0.57, proximidade);
    }
}
