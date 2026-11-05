using System.Reflection;
using System.Text.Json;
using Api.Agente.Domain.Efd.Catalogo;
using Api.Agente.Domain.Efd.Leiaute;

namespace Api.Agente.Domain.Efd;

/// <summary>Leiautes e catálogo versionados no repositório e embutidos no assembly (research R-05, R-06).</summary>
public sealed class DadosEfd
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private DadosEfd(CatalogoRegras catalogo, IReadOnlyList<DefinicaoLeiaute> leiautes)
    {
        Catalogo = catalogo;
        Leiautes = leiautes;
    }

    public CatalogoRegras Catalogo { get; }

    public IReadOnlyList<DefinicaoLeiaute> Leiautes { get; }

    public static DadosEfd Carregar()
    {
        var catalogo = Ler<CatalogoRegras>("catalogo-regras-efd-icms-ipi.json");
        catalogo.GarantirIntegridade();
        return new DadosEfd(catalogo, [Ler<DefinicaoLeiaute>("leiaute-efd-icms-ipi-020.json")]);
    }

    private static T Ler<T>(string recurso)
    {
        using var conteudo = Assembly.GetExecutingAssembly().GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"Recurso {recurso} não encontrado.");
        return JsonSerializer.Deserialize<T>(conteudo, Json)
            ?? throw new InvalidOperationException($"Recurso {recurso} inválido.");
    }
}
