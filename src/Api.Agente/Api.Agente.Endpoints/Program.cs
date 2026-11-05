using System.Text.Json.Serialization;
using Api.Agente.Endpoints.A2A;
using Api.Agente.Endpoints.Infraestrutura;
using Api.Agente.Endpoints.Regras;
using Api.Agente.Endpoints.Saude;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Endpoints.TabelasReferencia;
using Api.Agente.Endpoints.Validacoes;
using Api.Agente.Infrastructure.Configuracao;
using Api.Agente.Infrastructure.Observabilidade;
using Api.Agente.Infrastructure.Persistencia;
using Api.Agente.Infrastructure.Resiliencia;
using Api.Agente.Infrastructure.Validacao;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservabilidade("api-agente");
builder.Services
    .AddOpcoesAgente()
    .AddPersistenciaAgente()
    .AddClientesResilientes()
    .AddValidacaoEfd()
    .AddErrosPadronizados()
    .AddAutenticacaoAgente()
    .AddLimitacaoRequisicoes()
    .AddSaude()
    .AddA2aAgente(builder.Configuration);

// Campos nulos são omitidos: o contrato do envelope não aceita nulo em todos eles.
builder.Services.ConfigureHttpJsonOptions(opcoes =>
    opcoes.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

var app = builder.Build();

app.UseMiddleware<CorrelacaoMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapSaude();
app.MapValidacoes();
app.MapTabelasReferencia();
app.MapRegras();
app.MapA2aAgente(app.Configuration);

await app.RunAsync();

public partial class Program;
