using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Negocio;

/// <summary>
/// Coerência entre C100, C170 e C190 (FR-023), só em documentos regulares (COD_SIT 00 e 01): nas demais
/// situações, os valores são facultativos ou vazios (Guia, C100, Exceções 1, 3 e 4). A comparação com os
/// itens não vale para NF-e e NFC-e próprias, em que o C170 é excepcional ou parcial (Exceções 2, 6 e 9).
/// </summary>
public sealed class RegraTotaisDocumento : IValidadorEstrutural
{
    /// <summary>Campos do C100 que devem ser a soma do campo de mesmo nome nos C190.</summary>
    private static readonly string[] CamposAnaliticos = ["VL_BC_ICMS", "VL_ICMS", "VL_BC_ICMS_ST", "VL_ICMS_ST", "VL_IPI"];

    /// <summary>Campos do C190 que devem ser a soma do campo de mesmo nome nos C170 da mesma combinação.</summary>
    private static readonly string[] CamposItens = ["VL_BC_ICMS", "VL_ICMS", "VL_BC_ICMS_ST", "VL_ICMS_ST"];

    private Documento? _documento;

    private sealed record Combinacao(string Cst, string Cfop, decimal Aliquota);

    private sealed class Documento(long linha, string numero, DefinicaoRegistro definicao, string[] campos)
    {
        public long Linha { get; } = linha;
        public string Numero { get; } = numero;
        public DefinicaoRegistro Definicao { get; } = definicao;
        public string[] Campos { get; } = campos;
        public Dictionary<string, decimal> SomaAnaliticos { get; } = new(StringComparer.Ordinal);
        public Dictionary<Combinacao, Dictionary<string, decimal>> SomaPorCombinacaoAnalitica { get; } = [];
        public Dictionary<Combinacao, Dictionary<string, decimal>> SomaPorCombinacaoItens { get; } = [];
        public decimal SomaItens { get; set; }
        public decimal SomaOperacao { get; set; }
        public int QuantidadeItens { get; set; }
        public int QuantidadeAnaliticos { get; set; }

        /// <summary>Filho com valor inválido ou linha malformada: a soma não é confiável e nada é comparado.</summary>
        public bool ComFilhoInvalido { get; set; }

        public string Valor(string campo) => Definicao.Valor(Campos, campo);

        public bool ComparaItens => QuantidadeItens > 0 && !(Valor("IND_EMIT") == "0" && Valor("COD_MOD") is "55" or "65");
    }

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        switch (campos[0])
        {
            case "C100":
                Encerrar(contexto);
                if (definicao is not null && definicao.TemQuantidadeCorreta(campos) && definicao.Valor(campos, "COD_SIT") is "00" or "01")
                {
                    _documento = new Documento(linha.Numero, definicao.Valor(campos, "NUM_DOC"), definicao, campos);
                }

                break;
            case "C170" when _documento is not null:
                AcumularItem(campos, definicao, _documento);
                break;
            case "C190" when _documento is not null:
                AcumularAnalitico(campos, definicao, _documento);
                break;
            case "C990":
                Encerrar(contexto);
                break;
        }
    }

    public void Finalizar(ContextoValidacao contexto) => Encerrar(contexto);

    private static void AcumularItem(string[] campos, DefinicaoRegistro? definicao, Documento documento)
    {
        if (definicao is null
            || !definicao.TemQuantidadeCorreta(campos)
            || !TentarCombinacao(definicao, campos, out var combinacao)
            || !ValorEfd.TentarLer(definicao.Valor(campos, "VL_ITEM"), out var valorItem))
        {
            documento.ComFilhoInvalido = true;
            return;
        }

        documento.QuantidadeItens++;
        documento.SomaItens += valorItem;
        Somar(documento, documento.SomaPorCombinacaoItens, combinacao, CamposItens, definicao, campos);
    }

    private static void AcumularAnalitico(string[] campos, DefinicaoRegistro? definicao, Documento documento)
    {
        if (definicao is null
            || !definicao.TemQuantidadeCorreta(campos)
            || !TentarCombinacao(definicao, campos, out var combinacao)
            || !ValorEfd.TentarLer(definicao.Valor(campos, "VL_OPR"), out var operacao))
        {
            documento.ComFilhoInvalido = true;
            return;
        }

        documento.QuantidadeAnaliticos++;
        documento.SomaOperacao += operacao;
        foreach (var campo in CamposAnaliticos)
        {
            if (ValorEfd.TentarLer(definicao.Valor(campos, campo), out var valor))
            {
                documento.SomaAnaliticos[campo] = documento.SomaAnaliticos.GetValueOrDefault(campo) + valor;
            }
            else
            {
                documento.ComFilhoInvalido = true;
            }
        }

        Somar(documento, documento.SomaPorCombinacaoAnalitica, combinacao, CamposItens, definicao, campos);
    }

    private static void Somar(
        Documento documento,
        Dictionary<Combinacao, Dictionary<string, decimal>> destino,
        Combinacao combinacao,
        string[] nomes,
        DefinicaoRegistro definicao,
        string[] campos)
    {
        if (!destino.TryGetValue(combinacao, out var somas))
        {
            somas = new Dictionary<string, decimal>(StringComparer.Ordinal);
            destino[combinacao] = somas;
        }

        foreach (var nome in nomes)
        {
            if (ValorEfd.TentarLer(definicao.Valor(campos, nome), out var valor))
            {
                somas[nome] = somas.GetValueOrDefault(nome) + valor;
            }
            else
            {
                documento.ComFilhoInvalido = true;
            }
        }
    }

    /// <summary>A alíquota é comparada como número: 18 e 18,00 são a mesma combinação.</summary>
    private static bool TentarCombinacao(DefinicaoRegistro definicao, string[] campos, out Combinacao combinacao)
    {
        var valida = ValorEfd.TentarLer(definicao.Valor(campos, "ALIQ_ICMS"), out var aliquota);
        combinacao = new Combinacao(definicao.Valor(campos, "CST_ICMS"), definicao.Valor(campos, "CFOP"), aliquota);
        return valida;
    }

    private void Encerrar(ContextoValidacao contexto)
    {
        var documento = _documento;
        _documento = null;
        if (documento is null || documento.ComFilhoInvalido)
        {
            return;
        }

        if (documento.QuantidadeAnaliticos > 0)
        {
            ConferirAnaliticos(documento, contexto);
            ConferirValorDocumento(documento, contexto);
        }

        if (documento.ComparaItens)
        {
            ConferirValorMercadorias(documento, contexto);
            ConferirItensPorCombinacao(documento, contexto);
        }
    }

    private static void ConferirAnaliticos(Documento documento, ContextoValidacao contexto)
    {
        foreach (var campo in CamposAnaliticos)
        {
            Comparar(documento, RegrasNegocio.TotaisAnaliticos, campo, documento.SomaAnaliticos.GetValueOrDefault(campo), "soma dos C190", contexto);
        }
    }

    private static void ConferirValorDocumento(Documento documento, ContextoValidacao contexto) =>
        Comparar(documento, RegrasNegocio.ValorDocumento, "VL_DOC", documento.SomaOperacao, "soma de VL_OPR dos C190", contexto);

    private static void ConferirValorMercadorias(Documento documento, ContextoValidacao contexto) =>
        Comparar(documento, RegrasNegocio.ValorMercadorias, "VL_MERC", documento.SomaItens, "soma de VL_ITEM dos C170", contexto);

    private static void Comparar(Documento documento, string regra, string campo, decimal soma, string descricaoSoma, ContextoValidacao contexto)
    {
        if (!ValorEfd.TentarLer(documento.Valor(campo), out var informado) || informado == soma)
        {
            return;
        }

        contexto.Emitir(
            regra,
            $"Documento {documento.Numero}: {campo} informa {ValorEfd.Formatar(informado)}, e a {descricaoSoma} é {ValorEfd.Formatar(soma)} (diferença de {ValorEfd.Formatar(Math.Abs(informado - soma))}).",
            documento.Linha,
            "C100",
            campo,
            ValorEfd.Formatar(informado),
            ValorEfd.Formatar(soma));
    }

    private static void ConferirItensPorCombinacao(Documento documento, ContextoValidacao contexto)
    {
        foreach (var (combinacao, analitico) in documento.SomaPorCombinacaoAnalitica)
        {
            var descricao = $"CST {combinacao.Cst}, CFOP {combinacao.Cfop} e alíquota {ValorEfd.Formatar(combinacao.Aliquota)}";
            if (!documento.SomaPorCombinacaoItens.TryGetValue(combinacao, out var itens))
            {
                contexto.Emitir(
                    RegrasNegocio.ItensAnaliticos,
                    $"Documento {documento.Numero}: o C190 de {descricao} não tem C170 com a mesma combinação.",
                    documento.Linha,
                    "C190");
                continue;
            }

            foreach (var campo in CamposItens)
            {
                var somaAnalitico = analitico.GetValueOrDefault(campo);
                var somaItens = itens.GetValueOrDefault(campo);
                if (somaAnalitico != somaItens)
                {
                    contexto.Emitir(
                        RegrasNegocio.ItensAnaliticos,
                        $"Documento {documento.Numero}: no C190 de {descricao}, {campo} informa {ValorEfd.Formatar(somaAnalitico)}, e a soma dos C170 é {ValorEfd.Formatar(somaItens)} (diferença de {ValorEfd.Formatar(Math.Abs(somaAnalitico - somaItens))}).",
                        documento.Linha,
                        "C190",
                        campo,
                        ValorEfd.Formatar(somaAnalitico),
                        ValorEfd.Formatar(somaItens));
                }
            }
        }
    }
}
