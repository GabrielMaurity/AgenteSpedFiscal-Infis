using System.Globalization;
using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Estrutural;

/// <summary>Encerramentos de bloco, registro 9900 e registro 9999 contra as contagens reais (FR-014).</summary>
public sealed class ValidadorTotalizadores : IValidadorEstrutural
{
    private readonly Dictionary<string, long> _porRegistro = [];
    private readonly Dictionary<string, long> _porBloco = [];
    private readonly Dictionary<string, (long Quantidade, long Linha)> _declarados9900 = [];
    private readonly List<(string Bloco, string Registro, long Quantidade, long Linha)> _encerramentos = [];
    private (long Quantidade, long Linha)? _declarado9999;
    private long _totalLinhas;

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        var codigo = campos[0];
        _totalLinhas++;
        _porRegistro[codigo] = _porRegistro.GetValueOrDefault(codigo) + 1;

        if (definicao is null)
        {
            return;
        }

        // O 0000 soma no bloco 0 e o 9999 soma no bloco 9, mesmo sem pertencer a eles (Guia, registros 0990 e 9990).
        _porBloco[definicao.Bloco] = _porBloco.GetValueOrDefault(definicao.Bloco) + 1;

        if (codigo == "9900" && campos.Length > 2 && TentarQuantidade(campos[2], out var quantidade9900))
        {
            _declarados9900[campos[1]] = (quantidade9900, linha.Numero);
        }
        else if (codigo == "9999" && campos.Length > 1 && TentarQuantidade(campos[1], out var quantidade9999))
        {
            _declarado9999 = (quantidade9999, linha.Numero);
        }
        else if (contexto.Leiaute.Blocos.Any(b => b.Encerramento == codigo) && campos.Length > 1 && TentarQuantidade(campos[1], out var quantidade))
        {
            _encerramentos.Add((definicao.Bloco, codigo, quantidade, linha.Numero));
        }
    }

    public void Finalizar(ContextoValidacao contexto)
    {
        foreach (var (bloco, registro, quantidade, linha) in _encerramentos)
        {
            var real = _porBloco.GetValueOrDefault(bloco);
            if (quantidade != real)
            {
                contexto.Emitir(
                    RegrasEstruturais.TotalBloco,
                    $"O registro {registro} informa {quantidade} linhas no bloco {bloco}, e o bloco tem {real}.",
                    linha,
                    registro,
                    encontrado: Texto(quantidade),
                    esperado: Texto(real));
            }
        }

        foreach (var (registro, (quantidade, linha)) in _declarados9900)
        {
            var real = _porRegistro.GetValueOrDefault(registro);
            if (quantidade != real)
            {
                contexto.Emitir(
                    RegrasEstruturais.TotalRegistro9900,
                    $"O registro 9900 informa {quantidade} ocorrências de {registro}, e o arquivo tem {real}.",
                    linha,
                    "9900",
                    "QTD_REG_BLC",
                    Texto(quantidade),
                    Texto(real));
            }
        }

        foreach (var (registro, real) in _porRegistro.Where(p => !_declarados9900.ContainsKey(p.Key)))
        {
            contexto.Emitir(
                RegrasEstruturais.TotalRegistro9900,
                $"Falta a linha 9900 do registro {registro}, que ocorre {real} vezes no arquivo.",
                registro: "9900",
                campo: "REG_BLC",
                encontrado: null,
                esperado: registro);
        }

        if (_declarado9999 is { } total && total.Quantidade != _totalLinhas)
        {
            contexto.Emitir(
                RegrasEstruturais.TotalArquivo,
                $"O registro 9999 informa {total.Quantidade} linhas, e o arquivo tem {_totalLinhas}.",
                total.Linha,
                "9999",
                "QTD_LIN",
                Texto(total.Quantidade),
                Texto(_totalLinhas));
        }
    }

    private static bool TentarQuantidade(string valor, out long quantidade) =>
        long.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out quantidade);

    private static string Texto(long valor) => valor.ToString(CultureInfo.InvariantCulture);
}
