# Tasks: Publicação experimental em nuvem gratuita

**Feature**: `002-publicacao-cloud` | **Plano**: [plan.md](plan.md) | **Roteiro**: [quickstart.md](quickstart.md)

**[M]** = manual, feita pelo responsável nos painéis (nenhum segredo passa pelo chat ou pelo Git).
**[A]** = automatizável no repositório.

## Phase 1: Preparação

- [ ] T001 [M] Confirmar que o uso do Vercel Hobby pela INFIS é permitido (plano pessoal e não comercial); se não for, decidir plano pago ou outra plataforma antes de continuar (FR-P09)
- [ ] T002 [A] Criar a branch `002-publicacao-cloud` a partir da `main` com a feature 001 mergeada e rodar a suíte completa (`dotnet test`) como linha de base
- [X] T003 [A] Criar `deploy/vercel/api-agente/Dockerfile.vercel` e `deploy/vercel/api-rag/Dockerfile.vercel` (PORT da plataforma, usuário não root, imagem com shell)
- [X] T004 [A] Criar `deploy/vercel/publicar.ps1` e `deploy/vercel/variaveis.md`
- [X] T005 [A] Compilar as duas imagens localmente e conferir, com `PORT=9090`: `/health/live` 200, Swagger fechado, `401` sem token, usuário não root; retestado com o código da US3, Agent Card e `/mcp` com JWT (evidência no [plan.md](plan.md))
- [X] T006 [A] Criar `deploy/neon/01-bancos-e-roles.sql`

## Phase 2: Adaptação de código (exige aprovação)

- [X] T007 [A] Teste: com duas instâncias, a inicialização da segunda não altera execução em andamento da primeira, nem reenfileira ingestão que a primeira processa (FR-P06)
- [X] T008 [A] Limitar `FalharEmAndamentoAsync` (Api.Agente) e `RetomarPendentesAsync` (Api.Rag) a itens sem atualização há mais que um limiar configurável (inicial: 10 min, acima dos 300 s do Vercel), usando `AtualizadoEm`; padrão local inalterado
- [X] T009 [A] Garantir resposta `413` da própria API para arquivos acima de `Limites__TamanhoMaximoArquivoBytes` e `Ingestao__TamanhoMaximoArquivoBytes` com o limite de 4 MB (teste de integração)

## Phase 3: Bancos

- [X] T010 [M] Neon Free: criar projeto na região mais próxima do Vercel, executar `deploy/neon/01-bancos-e-roles.sql` pela conexão direta e conferir `vector` só no banco `rag`
- [X] T011 [M] Neon: criar uma branch de backup, aplicar as migrações das duas APIs rodando-as localmente com `Persistencia__AplicarMigracoesNaInicializacao=true` contra o Neon, e conferir as tabelas e a coluna `vector` com a dimensão do modelo de embeddings
- [ ] T012 [M] Atlas Free: criar cluster, usuário com `readWrite` só no banco `agente` e regra de acesso de rede; abrir `0.0.0.0/0` só com aprovação registrada em `docs/desvios/` (FR-P08)
- [ ] T013 [M] Atlas: teste de escrita e leitura com a connection string e conferência do índice TTL de `execucoes` e `conversas` após a primeira inicialização
- [ ] T014 [M] Ingerir o Guia 3.2.4 e o `faq-interno.md` rodando a Api.Rag localmente contra o Neon (FR-P05) e registrar a contagem de trechos

**Neon (2026-11-10)**: projeto `quiet-base-69847026`, branch `production`, região AWS us-east-1, vinculado com `neon init`/`neon deploy` (CLI 9.3.2). `01-bancos-e-roles.sql` executado pela conexão direta; `vector` 0.8.6 no `rag`. Migrações aplicadas pela conexão direta rodando as APIs localmente (rag: 2; agente: 1). Teste de ponta a ponta com a conexão **pooled**: ingestão do `faq-interno.md` (ativo, 4 trechos, `text-embedding-3-small` 1536), conversa com resposta `interno`, `fundamentadaEmFonteOficial = false`, `proximidade` 0,7641; consumo gravado em `consumo_ia` (2 chamadas) e `consumo_embeddings` (3). **Ressalvas**: a branch de backup da T011 não foi criada antes das migrações (banco vazio, sem perda); a senha do `neondb_owner` apareceu numa saída de comando e deve ser trocada no painel; o Guia 3.2.4 ainda não foi ingerido (T014).

## Phase 4: Publicação

- [ ] T015 [M] Criar os projetos `agente-sped-fiscal-api-rag` e `agente-sped-fiscal-api-agente` no Vercel **sem** integração automática com o Git e preencher as variáveis de [variaveis.md](../../deploy/vercel/variaveis.md)
- [ ] T016 [M] Publicar a Api.Rag (`publicar.ps1 -Api rag`), conferir `/health/ready` e `buscar_trechos` por MCP com o token de serviço
- [ ] T017 [M] Publicar a Api.Agente (`publicar.ps1 -Api agente`), conferir `/health/ready`, o Agent Card e `McpRag__Url` apontando para a Api.Rag publicada
- [ ] T018 [M] Medir: validação de `valido-perfil-a.txt` e de `unidade-sem-0190.txt` por REST e por A2A; tempo até o status final, cold start e se o processamento continua após o `202`

## Phase 5: Aceite

- [ ] T019 [M] Executar os [critérios de aceite](contracts/criterios-aceite.md) com evidências (URLs, respostas e trechos de log sem segredos)
- [ ] T020 [M] Testar o rollback (Instant Rollback do Vercel e restauração da branch do Neon) e registrar o tempo
- [ ] T021 [A] Registrar consumo e custos (OpenAI, quotas do Vercel, Atlas e Neon) e os riscos aceitos em `specs/002-publicacao-cloud/evidencias/`

**Ressalvas de T007/T008 (aprovadas em 2026-11-10)**: opções `Recuperacao:InstanciaUnica` (padrão `true`, comportamento local inalterado), `Recuperacao:LimiarMinutos` (10) e `Recuperacao:IntervaloVarreduraMinutos` (1). Na Api.Agente o limiar usa `AtualizadoEm` e, com várias instâncias, uma varredura periódica encerra execuções de instâncias que pararam. Na Api.Rag o limiar usa `IngeridoEm`, sem mudar o esquema; o reprocessamento de um documento antigo ainda pode ser retomado em paralelo por uma instância que inicie durante ele (risco aceito, demonstração). Testes: `StatusExecucaoTests`, `RecuperacaoVariasInstanciasTests` (MongoDB e PostgreSQL reais); suíte completa com 463 testes aprovados.

**T009 (2026-11-10)**: nenhuma mudança de código foi necessária; `LimiteVercelTests` nas duas APIs confirma `413` com `application/problem+json` acima de 4 MB. Suíte completa: 476 testes aprovados.

## Dependências

T001 bloqueia a Phase 4. T008 deve estar mergeada antes de T016/T017. T010 → T011 → T014. T012 → T013.
T016 antes de T017 (a Api.Agente precisa da URL da Api.Rag).
