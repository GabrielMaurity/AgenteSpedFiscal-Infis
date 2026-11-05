---
name: vercel-dotnet-pgvector-atlas
description: Preparar, validar e implantar APIs ASP.NET Core .NET 10 em Vercel usando contêineres Docker e bancos externos PostgreSQL com PgVector e MongoDB Atlas. Usar em features Spec Kit/SDD que peçam deploy, configuração de ambientes, migrations, secrets, diagnóstico de conectividade e testes de produção.
---

# Deploy ASP.NET Core .NET 10 + PgVector + MongoDB Atlas no Vercel

## Contrato
- Entrada: solução .NET 10, caminho do projeto API, endpoints, ambientes, provedor PostgreSQL compatível com `vector`, projeto/cluster Atlas e autorização de deploy.
- Saída: arquivos de infraestrutura consistentes, checklists e evidências de build/teste, URL de deploy e verificação HTTP/BD quando executados; nunca declarar deploy concluído sem prova.
- Referência técnica: Vercel suporta contêineres com `Dockerfile.vercel` e, desde 27/10/2026, `Dockerfile` raiz quando não há framework concorrente. Trata-se de runtime de funções com instâncias sem estado, não hospedagem persistente de PostgreSQL ou MongoDB.

## Arquitetura obrigatória
```
Cliente HTTP -> Vercel (container ASP.NET Core .NET 10)
                         |-- TLS --> PostgreSQL gerenciado + extensão vector
                         |-- TLS --> MongoDB Atlas
                         `-- TLS --> provedores LLM/MCP externos
```
Banco PostgreSQL/PgVector e Atlas devem estar fora do contêiner. Arquivos gravados localmente não são armazenamento durável. Verificar limitações de requests, tarefas de longa duração, WebSockets, região e custos de execução conforme plano atual; considerar host persistente para MCP Streamable HTTP com estado ou workers contínuos se necessário.

## 1. Pré-voo SDD
1. Ler `.specify/memory/constitution.md`, `specs/<feature>/spec.md`, `plan.md`, `tasks.md` e contracts quando existentes.
2. Identificar projeto `.csproj`, versão alvo (`net10.0`), porta HTTP, health check, dependências nativas e bibliotecas Npgsql/PgVector/MongoDB.
3. Identificar o provedor de PostgreSQL e validar `CREATE EXTENSION IF NOT EXISTS vector;` com permissões; não assumir que todo serviço PostgreSQL oferece a extensão.
4. Validar que APIs MCP e A2A externas são acessíveis e não dependem de hostnames do Docker Compose como `postgres` ou `mongodb`.
5. Confirmar autenticação, quotas, restrições de IP e políticas de rede de PostgreSQL e Atlas. Nunca recomendar liberar acesso universal permanentemente sem análise e controles compensatórios.

## 2. Dockerfile.vercel (.NET 10)
Criar na raiz do projeto Vercel e adaptar o caminho `src/Api.Agente/Api.Agente.csproj` à solução real. Em monorepo, não assumir que todos os serviços podem compartilhar automaticamente o mesmo endpoint.

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/Api.Agente/Api.Agente.csproj"
RUN dotnet publish "src/Api.Agente/Api.Agente.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.Agente.dll"]
```

**Porta:** verificar na documentação vigente do Vercel a forma de encaminhamento para o container; o serviço deve escutar na porta esperada pela plataforma, preferencialmente a variável `PORT` quando disponibilizada. Se necessário, adaptar a inicialização do ASP.NET Core para ler `PORT` e executar bind em `0.0.0.0`. Não assumir que `EXPOSE` sozinho configura roteamento.

Criar `.dockerignore` com `.git`, `.env`, `.env.*`, `**/bin`, `**/obj`, `.vs`, `.vercel`, `node_modules` e arquivos de segredos. Não incluir `.env.example` por engano se for necessário como documentação.

## 3. Configuração de ambiente e segurança
Variáveis recomendadas (nomes por configuração .NET):
- `ConnectionStrings__PgVector`: string PostgreSQL com TLS (`SSL Mode=Require` ou validação mais forte segundo provedor); sem credenciais no código.
- `ConnectionStrings__MongoDb`: connection string Atlas com TLS, usuário de privilégio mínimo e database separado por ambiente.
- `MongoDb__DatabaseName`: database para memória conversacional.
- `OpenAI__ApiKey`: chave de API, armazenada no painel de secrets do ambiente.
- `ASPNETCORE_ENVIRONMENT=Production`.

As credenciais de produção devem ser cadastradas em **Vercel Project Settings > Environment Variables** ou via CLI autenticado; nunca commitadas. Executar `vercel env ls` para conferir presença, não valores. Separar Preview e Production. Se o sistema usar outra nomenclatura, mapear para o `IConfiguration` real; não modificar appsettings sem avaliação.

## 4. Banco e migrações
- Usar Neon/Supabase/outro PostgreSQL gerenciado que suporte `pgvector`; validar a extensão em cada ambiente e usar pool de conexões compatível.
- Aplicar migrações por pipeline controlado ou tarefa administrativa antes do tráfego; não executar migrações concorrentes a cada cold start.
- Validar dimensão dos embeddings, índice HNSW/IVFFlat e distância (`vector_cosine_ops` etc.) conforme contrato e modelo de embedding.
- No Atlas, criar database/usuário de menor privilégio, configurar network access e índices para histórico (`usuarioId`, `createdAt`, `conversaId`), com retenção se aplicável.
- Usar timeouts, retry com backoff e gerenciamento de conexões; não criar `MongoClient` a cada request.

## 5. Validação local
```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
docker build -f Dockerfile.vercel -t api-agente:local .
docker run --rm -p 8080:8080 --env-file .env.production.local api-agente:local
```
Atenção: `.env.production.local` nunca deve ser commitado; o formato de variáveis precisa ser compatível com Docker; testar com credenciais de desenvolvimento isoladas sempre que possível. Testar `GET /health` sem expor segredos e endpoints funcionais com autenticação.

## 6. Vercel: publicação
```powershell
npm.cmd install -g vercel
vercel login
vercel link
vercel env ls
vercel deploy
# Promover somente depois dos testes de preview:
vercel deploy --prod
```
Conferir no Vercel o `Root Directory` correto, detecção do `Dockerfile.vercel`, permissões e suporte à funcionalidade de contêineres no plano. Para `Dockerfile` raiz, definir `"framework": "container"` no `vercel.json` apenas se necessário para evitar detecção concorrente. Não inventar regras `rewrites`, `services` ou portas sem checar configuração real. Em repositório conectado ao Vercel, pushes podem iniciar deploy automaticamente; evitar deploys duplicados.

## 7. Aceite pós-deploy
- Verificar URL de preview/produção, código HTTP do health check e autorização dos endpoints.
- Exercitar consulta vetorial real no PostgreSQL, gravação/leitura de memória no Atlas e chamada LLM/MCP se prevista; não despejar conteúdo sensível em logs.
- Verificar logs, latência, timeout, conexões TLS e possíveis erros de cold start.
- Preparar rollback para implantação anterior caso smoke tests falhem.
- Relatar URL, versão/commit, ambiente, testes executados e limitações observadas. Nunca afirmar sucesso sem evidência.

## Fontes oficiais para checagem antes de implementar
- https://vercel.com/docs/functions/runtimes
- https://vercel.com/kb/guide/does-vercel-support-docker-deployments
- https://vercel.com/changelog/deploy-a-container-from-a-plain-dockerfile
- https://vercel.com/docs/postgres
- https://www.mongodb.com/docs/atlas/
