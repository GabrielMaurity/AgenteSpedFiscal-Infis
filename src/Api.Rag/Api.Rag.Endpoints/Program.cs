using Api.Rag.Endpoints.Documentos;
using Api.Rag.Endpoints.Infraestrutura;
using Api.Rag.Endpoints.Mcp;
using Api.Rag.Endpoints.Saude;
using Api.Rag.Endpoints.Seguranca;
using Api.Rag.Infrastructure.Configuracao;
using Api.Rag.Infrastructure.Ingestao;
using Api.Rag.Infrastructure.Observabilidade;
using Api.Rag.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservabilidade("api-rag");
builder.Services
    .AddOpcoesRag()
    .AddPersistenciaRag()
    .AddBaseInformacao()
    .AddErrosPadronizados()
    .AddAutenticacaoRag(builder.Configuration, builder.Environment)
    .AddSaude()
    .AddDocumentacaoApi();

// Sem estado: cada chamada é independente, o que simplifica autenticação e réplicas (ADR-0005).
builder.Services
    .AddMcpServer()
    .WithHttpTransport(opcoes => opcoes.Stateless = true)
    .WithTools<FerramentasMcp>();

var app = builder.Build();

app.UseMiddleware<CorrelacaoMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDocumentacaoApi();
app.UseAuthentication();
app.UseAuthorization();

app.MapSaude();
app.MapDocumentos();
app.MapMcp("/mcp").RequireAuthorization(Politicas.ServicoMcp);

await app.RunAsync();

public partial class Program;
