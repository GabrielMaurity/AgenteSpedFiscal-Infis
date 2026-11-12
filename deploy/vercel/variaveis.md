# Variáveis de ambiente dos projetos Vercel

Cada API é um projeto Vercel próprio, com as próprias variáveis: nenhuma recebe a connection string
da outra (constitution P-07). Os nomes seguem os `IOptions` reais (`Configuracao/Opcoes.cs` de cada
API). **Valores só no painel do Vercel** (Settings → Environment Variables, tipo *Sensitive*), nunca
no Git, em Dockerfile ou em log. Rotacionar qualquer valor que vazar.

## Projeto `agente-sped-fiscal-api-agente`

| Variável | Valor | Sensível |
|---|---|---|
| `PORT` | `8080` (o Vercel encaminha para a 80 se não houver `PORT`) | não |
| `Postgres__ConnectionString` | `Host=<host-pooler-neon>;Database=agente;Username=agente;Password=<senha>;SSL Mode=Require;Channel Binding=Require` | **sim** |
| `MongoDb__ConnectionString` | `mongodb+srv://<usuario>:<senha>@<cluster>.mongodb.net/?retryWrites=true&w=majority` | **sim** |
| `MongoDb__Database` | `agente` | não |
| `Persistencia__AplicarMigracoesNaInicializacao` | `false` (migração é tarefa controlada, ver o roteiro) | não |
| `OpenAi__ApiKey` | chave da OpenAI | **sim** |
| `OpenAi__ModeloAnalise` | modelo do ADR-0003 | não |
| `McpRag__Url` | `https://<dominio-api-rag>.vercel.app/mcp` | não |
| `McpRag__TokenServico` | JWT com audiência `api-rag` | **sim** |
| `A2a__UrlBase` | `https://<dominio-api-agente>.vercel.app` | não |
| `Limites__TamanhoMaximoArquivoBytes` | `4000000` (abaixo do limite de 4,5 MB do Vercel) | não |
| `Recuperacao__InstanciaUnica` | `false` (várias instâncias: só recupera execuções paradas além do limiar) | não |
| `Recuperacao__LimiarMinutos` | `10` (acima dos 300 s do Vercel) | não |
| `DocumentacaoApi__Habilitada` | `true` publica `/swagger` e `/openapi/v1.json` sem token; endpoints seguem exigindo token (research R-40). Remover para desligar | não |
| `Authentication__Schemes__Bearer__ValidIssuer` | emissor dos tokens | não |
| `Authentication__Schemes__Bearer__ValidAudiences__0` | `api-agente` | não |
| `Authentication__Schemes__Bearer__SigningKeys__0__Issuer` | o mesmo emissor | não |
| `Authentication__Schemes__Bearer__SigningKeys__0__Value` | chave simétrica Base64 (≥ 32 bytes), **diferente da de desenvolvimento** | **sim** |

## Projeto `agente-sped-fiscal-api-rag`

| Variável | Valor | Sensível |
|---|---|---|
| `PORT` | `8080` | não |
| `Postgres__ConnectionString` | `Host=<host-pooler-neon>;Database=rag;Username=rag;Password=<senha>;SSL Mode=Require;Channel Binding=Require` | **sim** |
| `Persistencia__AplicarMigracoesNaInicializacao` | `false` | não |
| `OpenAi__ApiKey` | chave da OpenAI (embeddings) | **sim** |
| `Ingestao__TamanhoMaximoArquivoBytes` | `4000000` | não |
| `DocumentacaoApi__Habilitada` | `true` (research R-40) | não |
| `Recuperacao__InstanciaUnica` | `false` | não |
| `Recuperacao__LimiarMinutos` | `10` | não |
| `Authentication__Schemes__Bearer__ValidIssuer` | emissor dos tokens | não |
| `Authentication__Schemes__Bearer__ValidAudiences__0` | `api-rag` | não |
| `Authentication__Schemes__Bearer__SigningKeys__0__Issuer` | o mesmo emissor | não |
| `Authentication__Schemes__Bearer__SigningKeys__0__Value` | a mesma chave do projeto da Api.Agente | **sim** |

## Observações

- `OTEL_EXPORTER_OTLP_ENDPOINT` fica **sem valor**: sem ele, o OTLP não é ativado e os logs saem
  no stdout, uma linha JSON por registro e já mascarados (P-18), visíveis nos Runtime Logs do Vercel
  (`vercel logs <domínio> --scope csc-5b6f`). `Observabilidade__Console=true` força a saída no console
  mesmo com OTLP. EF Core, `HttpClient` e Polly ficam em Warning no `appsettings.json`.
- A chave simétrica e os tokens emitidos à mão servem só para a demonstração. Produção depende do
  provedor OIDC do ADR-0004.
- Em execução, use o host **pooled** do Neon (com `-pooler`), recomendado para serverless (skill
  neon-postgres); validado em 2026-11-10 com Npgsql/EF Core, ingestão, busca e conversa. Migrações
  rodam pelo host **direto** (sem `-pooler`), como passo controlado (T011).
- Senhas dos usuários `agente` e `rag` do Neon: `NEON_POSTGRES_AGENTE_PASSWORD` e
  `NEON_POSTGRES_RAG_PASSWORD` no `.env` local (não versionado).
- Para publicar pelo `publicar.ps1`, defina na sua máquina, fora do repositório: `VERCEL_TOKEN`,
  `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID_AGENTE` e `VERCEL_PROJECT_ID_RAG`.
