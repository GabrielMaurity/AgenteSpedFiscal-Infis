using System.Globalization;
using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;

namespace Api.Agente.Domain.Efd.Validacao.Estrutural;

/// <summary>Quantidade, tipo, tamanho, decimais e obrigatoriedade dos campos (FR-010).</summary>
public sealed class ValidadorCampos : IValidadorEstrutural
{
    /// <summary>Tamanho máximo de campo C sem indicação distinta (NT 2026.001, item 2.2.3).</summary>
    private const int TamanhoPadraoAlfanumerico = 255;

    public void Processar(LinhaEfd linha, string[] campos, DefinicaoRegistro? definicao, ContextoValidacao contexto)
    {
        if (definicao is null)
        {
            contexto.Emitir(
                RegrasEstruturais.RegistroDesconhecido,
                $"O registro {campos[0]} não existe no leiaute {contexto.Leiaute.CodVer}.",
                linha.Numero,
                campos[0]);
            return;
        }

        if (campos.Length != definicao.Campos.Count)
        {
            contexto.Emitir(
                RegrasEstruturais.QuantidadeCampos,
                $"O registro {definicao.Codigo} tem {campos.Length} campos, e o leiaute prevê {definicao.Campos.Count}.",
                linha.Numero,
                definicao.Codigo,
                encontrado: campos.Length.ToString(CultureInfo.InvariantCulture),
                esperado: definicao.Campos.Count.ToString(CultureInfo.InvariantCulture));
        }

        for (var i = 0; i < Math.Min(campos.Length, definicao.Campos.Count); i++)
        {
            ValidarCampo(linha.Numero, definicao, definicao.Campos[i], campos[i], contexto);
        }
    }

    public void Finalizar(ContextoValidacao contexto)
    {
    }

    private static void ValidarCampo(long numeroLinha, DefinicaoRegistro registro, DefinicaoCampo campo, string valor, ContextoValidacao contexto)
    {
        if (valor.Length == 0)
        {
            if (campo.EhObrigatorio)
            {
                contexto.Emitir(
                    RegrasEstruturais.CampoObrigatorio,
                    $"O campo obrigatório {campo.Nome} do registro {registro.Codigo} está vazio.",
                    numeroLinha,
                    registro.Codigo,
                    campo.Nome);
            }

            return;
        }

        if (campo.EhNumerico)
        {
            ValidarNumerico(numeroLinha, registro, campo, valor, contexto);
        }
        else
        {
            ValidarAlfanumerico(numeroLinha, registro, campo, valor, contexto);
        }
    }

    private static void ValidarNumerico(long numeroLinha, DefinicaoRegistro registro, DefinicaoCampo campo, string valor, ContextoValidacao contexto)
    {
        var virgula = valor.IndexOf(',', StringComparison.Ordinal);
        var inteiros = virgula < 0 ? valor : valor[..virgula];
        var decimais = virgula < 0 ? "" : valor[(virgula + 1)..];

        if (inteiros.Length == 0 || !inteiros.All(char.IsAsciiDigit) || !decimais.All(char.IsAsciiDigit))
        {
            contexto.Emitir(
                RegrasEstruturais.CampoNumerico,
                $"O campo numérico {campo.Nome} do registro {registro.Codigo} tem caracteres inválidos.",
                numeroLinha,
                registro.Codigo,
                campo.Nome,
                valor);
            return;
        }

        var maximoDecimais = campo.Decimais ?? 0;
        if (decimais.Length > maximoDecimais)
        {
            contexto.Emitir(
                RegrasEstruturais.CasasDecimais,
                $"O campo {campo.Nome} do registro {registro.Codigo} tem {decimais.Length} casas decimais; o máximo é {maximoDecimais}.",
                numeroLinha,
                registro.Codigo,
                campo.Nome,
                valor,
                maximoDecimais.ToString(CultureInfo.InvariantCulture));
        }

        if (campo.Tamanho is { } tamanho)
        {
            ValidarTamanho(numeroLinha, registro, campo, valor, inteiros.Length, tamanho, contexto);
        }
    }

    private static void ValidarAlfanumerico(long numeroLinha, DefinicaoRegistro registro, DefinicaoCampo campo, string valor, ContextoValidacao contexto)
    {
        if (valor.Any(c => c < ' '))
        {
            contexto.Emitir(
                RegrasEstruturais.CampoAlfanumerico,
                $"O campo {campo.Nome} do registro {registro.Codigo} tem caractere não imprimível.",
                numeroLinha,
                registro.Codigo,
                campo.Nome);
        }

        ValidarTamanho(numeroLinha, registro, campo, valor, valor.Length, campo.Tamanho ?? TamanhoPadraoAlfanumerico, contexto);
    }

    private static void ValidarTamanho(
        long numeroLinha, DefinicaoRegistro registro, DefinicaoCampo campo, string valor, int tamanhoReal, int tamanho, ContextoValidacao contexto)
    {
        var invalido = campo.TamanhoExato ? tamanhoReal != tamanho : tamanhoReal > tamanho;
        if (invalido)
        {
            contexto.Emitir(
                RegrasEstruturais.TamanhoCampo,
                $"O campo {campo.Nome} do registro {registro.Codigo} tem {tamanhoReal} caracteres; o leiaute prevê {(campo.TamanhoExato ? "exatamente" : "no máximo")} {tamanho}.",
                numeroLinha,
                registro.Codigo,
                campo.Nome,
                valor,
                tamanho.ToString(CultureInfo.InvariantCulture));
        }
    }
}
