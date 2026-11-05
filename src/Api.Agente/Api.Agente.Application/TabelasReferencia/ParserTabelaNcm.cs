using System.Globalization;
using System.Text;
using System.Text.Json;
using Api.Agente.Domain.Efd.Validacao.Negocio;

namespace Api.Agente.Application.TabelasReferencia;

public sealed record LinhaNcm(string Codigo, string Descricao, DateOnly? VigenciaInicio, DateOnly? VigenciaFim)
{
    public CodigoNcm ParaCodigo() => new(Codigo, VigenciaInicio, VigenciaFim);
}

public sealed class TabelaNcmInvalidaException(string mensagem) : Exception(mensagem);

/// <summary>
/// Lê a tabela em dois formatos: JSON com a lista "Nomenclaturas" (campos Codigo, Descricao, Data_Inicio e
/// Data_Fim) e texto delimitado por ";", "|" ou tabulação (código, descrição e, opcionalmente, início e fim
/// da vigência). Só códigos de 8 dígitos entram: as posições de capítulo e subposição não são NCM de item.
/// </summary>
public static class ParserTabelaNcm
{
    private const int DigitosNcm = 8;

    /// <summary>Data usada como "sem fim de vigência" em tabelas oficiais.</summary>
    private static readonly DateOnly SemFim = new(9999, 12, 31);

    private static readonly string[] FormatosData = ["dd/MM/yyyy", "yyyy-MM-dd"];

    public static IReadOnlyList<LinhaNcm> Ler(byte[] conteudo)
    {
        var texto = Decodificar(conteudo).TrimStart('﻿', ' ', '\r', '\n', '\t');
        var linhas = texto.StartsWith('{') || texto.StartsWith('[') ? LerJson(texto) : LerDelimitado(texto);
        if (linhas.Count == 0)
        {
            throw new TabelaNcmInvalidaException("O arquivo não tem nenhum código de NCM de 8 dígitos.");
        }

        return linhas;
    }

    private static string Decodificar(byte[] conteudo)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(conteudo);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(conteudo);
        }
    }

    private static List<LinhaNcm> LerJson(string texto)
    {
        try
        {
            using var documento = JsonDocument.Parse(texto);
            var raiz = documento.RootElement;
            var itens = raiz.ValueKind == JsonValueKind.Array ? raiz : Propriedade(raiz, "Nomenclaturas");
            if (itens is not { ValueKind: JsonValueKind.Array } lista)
            {
                throw new TabelaNcmInvalidaException("O JSON não tem a lista 'Nomenclaturas'.");
            }

            var linhas = new List<LinhaNcm>();
            foreach (var item in lista.EnumerateArray())
            {
                var linha = Montar(Texto(item, "Codigo"), Texto(item, "Descricao"), Texto(item, "Data_Inicio"), Texto(item, "Data_Fim"));
                if (linha is not null)
                {
                    linhas.Add(linha);
                }
            }

            return linhas;
        }
        catch (JsonException)
        {
            throw new TabelaNcmInvalidaException("O arquivo JSON da tabela de NCM é inválido.");
        }
    }

    private static List<LinhaNcm> LerDelimitado(string texto)
    {
        var linhas = new List<LinhaNcm>();
        foreach (var bruta in texto.Split('\n'))
        {
            var partes = bruta.TrimEnd('\r').Trim('|').Split([';', '|', '\t']);
            var linha = partes.Length < 2
                ? null
                : Montar(partes[0], partes[1], partes.ElementAtOrDefault(2) ?? "", partes.ElementAtOrDefault(3) ?? "");
            if (linha is not null)
            {
                linhas.Add(linha);
            }
        }

        return linhas;
    }

    /// <summary>Retorna nulo para cabeçalho e para posições sem 8 dígitos.</summary>
    private static LinhaNcm? Montar(string codigoBruto, string descricao, string inicio, string fim)
    {
        var codigo = codigoBruto.Trim().Replace(".", "", StringComparison.Ordinal);
        if (codigo.Length != DigitosNcm || !codigo.All(char.IsAsciiDigit))
        {
            return null;
        }

        var vigenciaFim = LerData(fim);
        return new LinhaNcm(codigo, descricao.Trim(), LerData(inicio), vigenciaFim == SemFim ? null : vigenciaFim);
    }

    private static DateOnly? LerData(string texto)
    {
        var limpo = texto.Trim();
        if (limpo.Length == 0)
        {
            return null;
        }

        return DateOnly.TryParseExact(limpo, FormatosData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data
            : throw new TabelaNcmInvalidaException("A tabela de NCM tem data de vigência inválida.");
    }

    private static JsonElement? Propriedade(JsonElement elemento, string nome)
    {
        if (elemento.ValueKind == JsonValueKind.Object)
        {
            foreach (var propriedade in elemento.EnumerateObject())
            {
                if (propriedade.Name.Equals(nome, StringComparison.OrdinalIgnoreCase))
                {
                    return propriedade.Value;
                }
            }
        }

        return null;
    }

    private static string Texto(JsonElement elemento, string nome) =>
        Propriedade(elemento, nome) is { ValueKind: JsonValueKind.String } valor ? valor.GetString() ?? "" : "";
}
