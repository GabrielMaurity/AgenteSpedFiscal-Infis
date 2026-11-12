# Roteiro de publicação (Vercel Hobby + Atlas Free + Neon Free)

Roteiro para o responsável executar. Nenhuma conta, recurso ou deploy é criado por este repositório.
"Gratuito" significa dentro das quotas de cada plano, que mudam; confira antes de começar:
[Vercel Hobby](https://vercel.com/docs/plans/hobby) ·
[limites das Functions](https://vercel.com/docs/functions/limitations) ·
[Atlas Free](https://www.mongodb.com/docs/atlas/reference/free-shared-limitations/) ·
[Neon pricing](https://neon.com/pricing) · [pgvector no Neon](https://neon.com/docs/extensions/pgvector).
Custos da OpenAI não estão incluídos.

## 1. Neon (PostgreSQL + pgvector)

1. Criar projeto Free na região mais próxima do Vercel (padrão do Vercel: `iad1`, EUA leste).
2. Copiar a connection string **direta** (host sem `-pooler`) do usuário dono.
3. Executar, passando as senhas como variáveis do shell (nunca no arquivo):
   `psql "<conexao-direta>?sslmode=require" -v agente_senha="..." -v rag_senha="..." -f deploy/neon/01-bancos-e-roles.sql`
4. Criar uma branch de backup no painel do Neon antes das migrações.
5. Aplicar as migrações rodando cada API localmente (`dotnet run`) com
   `Persistencia__AplicarMigracoesNaInicializacao=true` e a connection string do Neon com
   `SSL Mode=Require`; conferir as tabelas e a coluna `vector`.
6. Ingerir o Guia 3.2.4 pela Api.Rag local (o PDF passa do limite de 4,5 MB do Vercel).

## 2. MongoDB Atlas

1. Criar cluster Free; criar usuário de banco com `readWrite` só no banco `agente`.
2. Network Access: o Vercel não tem IP de saída fixo no Hobby. Abrir `0.0.0.0/0` **só** com aprovação
   registrada em `docs/desvios/`, senha forte, TLS (padrão do `mongodb+srv`) e prazo para revisão.
3. Copiar a string `mongodb+srv://...`; escapar caracteres especiais da senha.

## 3. Vercel

1. Criar dois projetos (Add New → Project), sem conectar o Git para deploy automático:
   `agente-sped-fiscal-api-rag` e `agente-sped-fiscal-api-agente`. Anotar o ID de cada um
   (Settings → General) e o ID da conta/time.
2. Preencher as variáveis de [deploy/vercel/variaveis.md](../../deploy/vercel/variaveis.md) em cada
   projeto, como *Sensitive* quando indicado.
3. Na sua máquina, definir `VERCEL_TOKEN`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID_RAG` e
   `VERCEL_PROJECT_ID_AGENTE` e publicar, primeiro a Api.Rag:

   ```powershell
   ./deploy/vercel/publicar.ps1 -Api rag -Producao
   ./deploy/vercel/publicar.ps1 -Api agente -Producao
   ```

4. Preencher `McpRag__Url` e `A2a__UrlBase` com os domínios gerados e publicar a Api.Agente de novo.

## 4. Verificação e rollback

Executar os [critérios de aceite](contracts/criterios-aceite.md). Para voltar: Instant Rollback no
painel do Vercel (Deployments → deploy anterior → Promote) e, se uma migração causou o problema,
restaurar a branch de backup do Neon.
