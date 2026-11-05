using System.Globalization;
using Api.Agente.Domain.Achados;

namespace Api.Agente.Domain.Efd.Catalogo;

public sealed record FonteOficialCatalogo(string Documento, string Versao, string? Ato);

public sealed record AplicacaoRegra(
    IReadOnlyList<string> CodVer,
    string PeriodoInicio,
    string? PeriodoFim,
    IReadOnlyList<string>? Perfis,
    IReadOnlyList<string>? Ufs);

public sealed record RegraCatalogo(
    string Id,
    string Descricao,
    string Tipo,
    string Severidade,
    string Status,
    AplicacaoRegra Aplicacao,
    Fonte? Fonte,
    string? OrigemCandidata,
    IReadOnlyList<string>? DependeDe,
    string AcaoCorretiva)
{
    public bool EstaAtiva => Status == "ativa";

    public Categoria Categoria => Tipo == "estrutural" ? Categoria.ErroEstrutural : Categoria.InconsistenciaNegocio;

    public Severidade SeveridadeAchado => Severidade switch
    {
        "impeditiva" => Achados.Severidade.Impeditiva,
        "advertencia" => Achados.Severidade.Advertencia,
        _ => Achados.Severidade.Informativa,
    };

    /// <summary>A regra vale para a versão, o período (AAAA-MM), o perfil e a UF do arquivo.</summary>
    public bool SeAplica(string codVer, DateOnly periodoFim, string perfil, string uf)
    {
        var periodo = periodoFim.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return Aplicacao.CodVer.Contains(codVer)
            && string.CompareOrdinal(periodo, Aplicacao.PeriodoInicio) >= 0
            && (Aplicacao.PeriodoFim is null || string.CompareOrdinal(periodo, Aplicacao.PeriodoFim) <= 0)
            && (Aplicacao.Perfis is null || Aplicacao.Perfis.Contains(perfil))
            && (Aplicacao.Ufs is null || Aplicacao.Ufs.Contains(uf));
    }
}

/// <summary>Catálogo versionado. Regra ativa sem fonte oficial completa impede o carregamento (FR-030).</summary>
public sealed record CatalogoRegras(
    string VersaoCatalogo,
    IReadOnlyList<FonteOficialCatalogo> FontesOficiais,
    IReadOnlyList<RegraCatalogo> Regras)
{
    public void GarantirIntegridade()
    {
        var repetidas = Regras.GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repetidas.Count > 0)
        {
            throw new InvalidOperationException($"Regras repetidas no catálogo: {string.Join(", ", repetidas)}.");
        }

        foreach (var regra in Regras.Where(r => r.EstaAtiva))
        {
            if (regra.Fonte is null)
            {
                throw new InvalidOperationException($"Regra ativa {regra.Id} sem fonte oficial.");
            }

            if (!FontesOficiais.Any(f => f.Documento == regra.Fonte.Documento && f.Versao == regra.Fonte.Versao))
            {
                throw new InvalidOperationException(
                    $"Regra ativa {regra.Id} cita fonte fora de fontesOficiais: {regra.Fonte.Documento} {regra.Fonte.Versao}.");
            }
        }
    }
}
