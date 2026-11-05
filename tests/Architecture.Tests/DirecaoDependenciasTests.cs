using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace Architecture.Tests;

/// <summary>Direção de dependências da constitution P-07 e P-08.</summary>
public class DirecaoDependenciasTests
{
    private static readonly string[] ProibidosNoDominio =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.SemanticKernel",
        "MongoDB",
        "Npgsql",
        "Pgvector",
        "ModelContextProtocol",
    ];

    public static TheoryData<string> Dominios => new() { "Api.Agente.Domain", "Api.Rag.Domain" };

    public static TheoryData<string> Aplicacoes => new() { "Api.Agente.Application", "Api.Rag.Application" };

    private static Assembly Carregar(string nome) =>
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, $"{nome}.dll"));

    private static string CaminhoProjeto(string nome)
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (diretorio is not null && !File.Exists(Path.Combine(diretorio.FullName, "AgenteSpedFiscal.slnx")))
        {
            diretorio = diretorio.Parent;
        }

        Assert.NotNull(diretorio);
        var servico = nome.StartsWith("Api.Agente", StringComparison.Ordinal) ? "Api.Agente" : "Api.Rag";
        return Path.Combine(diretorio.FullName, "src", servico, nome, $"{nome}.csproj");
    }

    private static string[] Referencias(string nome, string elemento) =>
        XDocument.Load(CaminhoProjeto(nome)).Descendants(elemento)
            .Select(e => (string?)e.Attribute("Include") ?? "")
            .ToArray();

    [Theory]
    [MemberData(nameof(Dominios))]
    public void Dominio_nao_depende_de_infraestrutura_nem_de_sdks(string dominio)
    {
        var resultado = Types.InAssembly(Carregar(dominio))
            .ShouldNot().HaveDependencyOnAny(ProibidosNoDominio)
            .GetResult();

        Assert.True(resultado.IsSuccessful, string.Join(", ", resultado.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(Dominios))]
    public void Projeto_de_dominio_nao_tem_referencias(string dominio)
    {
        Assert.Empty(Referencias(dominio, "PackageReference"));
        Assert.Empty(Referencias(dominio, "ProjectReference"));
        Assert.Empty(Referencias(dominio, "FrameworkReference"));
    }

    [Theory]
    [MemberData(nameof(Aplicacoes))]
    public void Aplicacao_nao_depende_de_infraestrutura_nem_de_sdks(string aplicacao)
    {
        var infraestrutura = aplicacao.Replace("Application", "Infrastructure", StringComparison.Ordinal);

        var resultado = Types.InAssembly(Carregar(aplicacao))
            .ShouldNot().HaveDependencyOnAny([.. ProibidosNoDominio, infraestrutura])
            .GetResult();

        Assert.True(resultado.IsSuccessful, string.Join(", ", resultado.FailingTypeNames ?? []));
        Assert.DoesNotContain(Referencias(aplicacao, "ProjectReference"), r => r.Contains("Infrastructure", StringComparison.Ordinal));
        Assert.Empty(Referencias(aplicacao, "PackageReference"));
    }

    [Theory]
    [InlineData("Api.Agente.Domain", "Api.Rag")]
    [InlineData("Api.Agente.Application", "Api.Rag")]
    [InlineData("Api.Agente.Infrastructure", "Api.Rag")]
    [InlineData("Api.Agente.Endpoints", "Api.Rag")]
    [InlineData("Api.Rag.Domain", "Api.Agente")]
    [InlineData("Api.Rag.Application", "Api.Agente")]
    [InlineData("Api.Rag.Infrastructure", "Api.Agente")]
    [InlineData("Api.Rag.Endpoints", "Api.Agente")]
    public void Servicos_nao_referenciam_um_ao_outro(string projeto, string outroServico)
    {
        Assert.DoesNotContain(Referencias(projeto, "ProjectReference"), r => r.Contains(outroServico, StringComparison.Ordinal));
    }

    [Fact]
    public void Api_agente_nao_acessa_pgvector_nem_dados_da_api_rag()
    {
        Assert.DoesNotContain("Pgvector.EntityFrameworkCore", Referencias("Api.Agente.Infrastructure", "PackageReference"));
    }
}
