using Api.Rag.Endpoints.Infraestrutura;
using Api.Rag.Endpoints.Saude;
using Api.Rag.Endpoints.Seguranca;
using Api.Rag.Infrastructure.Configuracao;
using Api.Rag.Infrastructure.Observabilidade;
using Api.Rag.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservabilidade("api-rag");
builder.Services
    .AddOpcoesRag()
    .AddPersistenciaRag()
    .AddErrosPadronizados()
    .AddAutenticacaoRag()
    .AddSaude();

var app = builder.Build();

app.UseMiddleware<CorrelacaoMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapSaude();

await app.RunAsync();

public partial class Program;
