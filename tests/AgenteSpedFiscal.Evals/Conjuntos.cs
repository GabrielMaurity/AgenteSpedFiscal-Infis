using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenteSpedFiscal.Evals;

/// <summary>Localização esperada: um dos registros, ou o capítulo com uma das seções.</summary>
public sealed record Esperado(string[]? Registros, string? Capitulo, string[]? Secoes);

public sealed record ItemRecuperacao(string Id, string Pergunta, string Periodo, Esperado Esperado);

public sealed record Criterios(
    bool? Fundamentada,
    bool? FundamentadaEmFonteOficial,
    string[]? Classificacoes,
    string[]? RegistrosCitados,
    string[]? Contem,
    string[]? NaoContem,
    string? ContemLimitacao,
    bool? ComLimitacao);

public sealed record ItemConversa(string Id, string Pergunta, string? Periodo, Criterios Criterios);

public sealed record Conjunto<T>(string Descricao, IReadOnlyList<T> Itens);

/// <summary>Conjuntos versionados em evals/&lt;nome&gt;/ (research R-17).</summary>
public static class Conjuntos
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly string[] Conversas = ["explicacoes", "injecao", "sem-resposta", "interno"];

    public static Conjunto<T> Ler<T>(string diretorio, string nome)
    {
        var caminho = Path.Combine(diretorio, $"{nome}.json");
        return JsonSerializer.Deserialize<Conjunto<T>>(File.ReadAllText(caminho), Json)
            ?? throw new InvalidDataException($"Conjunto vazio: {caminho}");
    }
}
