using Api.Agente.Infrastructure.Observabilidade;

namespace Api.Agente.Integration.Tests.Infraestrutura;

public class MascaradorDadosSensiveisTests
{
    [Theory]
    [InlineData("CNPJ 12.345.678/0001-95 inválido", "CNPJ *** inválido")]
    [InlineData("CNPJ 12345678000195 inválido", "CNPJ *** inválido")]
    [InlineData("CNPJ AB.CDE.FGH/0001-95 inválido", "CNPJ *** inválido")]
    [InlineData("CNPJ ABCDEFGH000195 inválido", "CNPJ *** inválido")]
    [InlineData("CPF 123.456.789-09 inválido", "CPF *** inválido")]
    [InlineData("CPF 12345678909 inválido", "CPF *** inválido")]
    public void Mascara_documentos(string entrada, string esperado) =>
        Assert.Equal(esperado, MascaradorDadosSensiveis.Mascarar(entrada));

    [Fact]
    public void Mascara_o_conteudo_de_linha_efd_e_mantem_o_registro()
    {
        var resultado = MascaradorDadosSensiveis.Mascarar("Linha |0150|001|FORNECEDOR LTDA|1058|12345678000195||");

        Assert.Equal("Linha |0150|***", resultado);
    }

    [Theory]
    [InlineData("Execução 019a5c2e-7b1f-7c3a-9d4e-1f2a3b4c5d6e concluída")]
    [InlineData("Regra EFD-ESTR-001 na linha 42 do registro K990")]
    public void Preserva_textos_operacionais(string entrada) =>
        Assert.Equal(entrada, MascaradorDadosSensiveis.Mascarar(entrada));
}
