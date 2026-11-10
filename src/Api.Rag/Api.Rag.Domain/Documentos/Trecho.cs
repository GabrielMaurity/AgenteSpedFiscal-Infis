using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Api.Rag.Domain.Documentos;

/// <summary>Onde o trecho está no documento: usado na citação (FR-051).</summary>
public sealed record LocalizacaoTrecho(string? Capitulo, string? Secao, string? Registro, int? PaginaInicio, int? PaginaFim);

/// <summary>Parte de um documento, já segmentada, antes de receber o embedding.</summary>
public sealed record Trecho(int Ordem, string Texto, LocalizacaoTrecho Localizacao)
{
    public string TextoHash { get; } = TextoNormalizado.Hash(Texto);
}

/// <summary>Deduplicação por texto normalizado: espaços e quebras de linha não distinguem trechos (research R-18).</summary>
public static partial class TextoNormalizado
{
    public static string Normalizar(string texto) => Espacos().Replace(texto, " ").Trim();

    public static string Hash(string texto) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalizar(texto))));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();
}
