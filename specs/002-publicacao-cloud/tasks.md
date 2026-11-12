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
- [X] T012 [M] Atlas Free: criar cluster, usuário com `readWrite` só no banco `agente` e regra de acesso de rede; abrir `0.0.0.0/0` só com aprovação registrada em `docs/desvios/` (FR-P08)
- [X] T013 [M] Atlas: teste de escrita e leitura com a connection string e conferência do índice TTL de `execucoes` e `conversas` após a primeira inicialização
- [X] T014 [M] Ingerir o Guia 3.2.4 e o `faq-interno.md` rodando a Api.Rag localmente contra o Neon (FR-P05) e registrar a contagem de trechos

**Neon (2026-11-10)**: projeto `quiet-base-69847026`, branch `production`, região AWS us-east-1, vinculado com `neon init`/`neon deploy` (CLI 9.3.2). `01-bancos-e-roles.sql` executado pela conexão direta; `vector` 0.8.6 no `rag`. Migrações aplicadas pela conexão direta rodando as APIs localmente (rag: 2; agente: 1). Teste de ponta a ponta com a conexão **pooled**: ingestão do `faq-interno.md` (ativo, 4 trechos, `text-embedding-3-small` 1536), conversa com resposta `interno`, `fundamentadaEmFonteOficial = false`, `proximidade` 0,7641; consumo gravado em `consumo_ia` (2 chamadas) e `consumo_embeddings` (3). **Ressalvas**: a branch de backup da T011 não foi criada antes das migrações (banco vazio, sem perda); a senha do `neondb_owner` apareceu numa saída de comando e deve ser trocada no painel; o Guia 3.2.4 ainda não foi ingerido (T014).

**Atlas (2026-11-12)**: cluster `Cluster0` (MongoDB 8.0.37), string `mongodb+srv` em `ATLAS_MONGODB_URI` no `.env` local. T013: ping, escrita, leitura e remoção no banco `agente`; a Api.Agente, rodando localmente contra Atlas e Neon, ficou pronta (`/health/ready` 200), criou os índices de `execucoes` e `conversas` com TTL em `expiraEm` e gravou uma conversa (`201`). **T012 concluída (2026-11-12)**: usuário de banco só com `readWrite@agente` (teste de gravação fora do banco `agente` negado com `AtlasError`); regra de rede `0.0.0.0/0` criada pelo responsável, conforme [docs/desvios/0001-atlas-acesso-de-rede-aberto.md](../../docs/desvios/0001-atlas-acesso-de-rede-aberto.md) (revisão até 2027-02-12). O acesso a partir do Vercel será confirmado na publicação (T017).

**T014 (2026-11-12)**: Guia Prático EFD ICMS/IPI 3.2.4 (PDF de 6,5 MB, 366 páginas) ingerido pela Api.Rag local contra o Neon pooled: `ativo`, oficial, **723 trechos**, `text-embedding-3-small` (1536), em cerca de 40 s. Título corrigido no banco (acento perdido no envio pelo `curl`). Busca MCP de "O valor de IBS e CBS entra no VL_DOC do C100 em 2026?" com `periodo=2026-03` trouxe Cap. I, Seção 10 (similaridade 0,5715) e trechos do C100. Base no Neon: Guia (oficial, 723) e FAQ interno (4).

## Phase 4: Publicação

- [X] T015 [M] Criar os projetos `agente-sped-fiscal-api-rag` e `agente-sped-fiscal-api-agente` no Vercel **sem** integração automática com o Git e preencher as variáveis de [variaveis.md](../../deploy/vercel/variaveis.md)
- [X] T016 [M] Publicar a Api.Rag (`publicar.ps1 -Api rag`), conferir `/health/ready` e `buscar_trechos` por MCP com o token de serviço
- [X] T017 [M] Publicar a Api.Agente (`publicar.ps1 -Api agente`), conferir `/health/ready`, o Agent Card e `McpRag__Url` apontando para a Api.Rag publicada
- [X] T018 [M] Medir: validação de `valido-perfil-a.txt` e de `unidade-sem-0190.txt` por REST e por A2A; tempo até o status final, cold start e se o processamento continua após o `202`

**Publicação (2026-11-12)**: projetos `agente-sped-fiscal-api-rag` (`prj_T0asZ2gAnfmFphWK6nswbKFgoP1P`) e `agente-sped-fiscal-api-agente` (`prj_9cBzm04r97yk1y8l0dNvZQ1whFmB`) no time CSC (Hobby), região iad1, Fluid compute, sem Git. Ajustes necessários: framework `container` (com o preset "Other" o Vercel publicou estático, 404 em tudo) e proteção trocada de `all_except_custom_domains` para Standard (`prod_deployment_urls_and_all_previews`), senão o domínio de produção exigia login do Vercel. 11 variáveis na Api.Rag e 17 na Api.Agente, só em Production, segredos como *Sensitive*, enviadas pela CLI a partir do `.env` (chave JWT nova, emissor `agente-sped-fiscal-vercel`, tokens de serviço e de teste). `publicar.ps1` passou a usar a sessão do `vercel login`.

Evidências nas URLs públicas: health live/ready 200 nas duas APIs (Neon e Atlas acessíveis; confirma a regra do desvio 0001); Swagger e endpoints sem token 401; Api.Rag: documentos 200 com token admin e 403 sem `admin_base`, `buscar_trechos` por MCP 200 com token de serviço e 401 com token inválido; Api.Agente: Agent Card 200 com as duas skills e URL `https`, conversa "O valor de IBS entra no VL_DOC do C100 em 2026?" fundamentada em fonte oficial (Cap. I, Seção 10) em 6–10 s, `proximidade` 0,5372; validação de `valido-perfil-a.txt` e `unidade-sem-0190.txt` por REST: `202` em menos de 0,5 s e status final em 7–11 s, com análise da IA aceita — **o processamento após o `202` funciona no Vercel**. `EFD-NEG-003` aparece como não realizada porque a tabela de NCM ainda não foi importada no Neon.

**Correção encontrada em produção**: a `proximidade` vinha 0 quando o modelo relia um trecho por `obter_trecho` (sem consulta), que sobrescrevia a similaridade. `ContextoRecuperacao.Registrar` passou a preservar a primeira similaridade conhecida; testes em `ContextoRecuperacaoTests`.

**Logs (2026-11-12)**: sem OTLP, as APIs passam a escrever cada log no stdout como uma linha JSON (timestamp, nível, categoria, mensagem, traceId, exceção), depois do mascaramento e de forma síncrona (`ExportadorLogConsole`, `ObservabilidadeExtensions.ConfigurarLogs`; testes `LogConsoleTests` nas duas APIs). EF Core, `HttpClient` e Polly em Warning: uma conversa caiu de cerca de 100 para 15 linhas nos Runtime Logs; varredura sem CNPJ, linha EFD, token ou chave.

**Novos documentos em produção (2026-11-12)**: ingeridos pela URL pública com o token admin. Blog TecnoSpeed (interno, 2 trechos) ativo de primeira. Manual Consolidado MG v.2025.02 (2,5 MB, 186 páginas, oficial/`outro_oficial`) falhou: a OpenAI recusou com 400 trechos acima de 8.191 tokens; com o teto de 4.000 tokens por trecho (research R-39) foi reprocessado e ficou ativo com 85 trechos em cerca de 10 s. A busca passou a incluir outros tipos oficiais: a pergunta sobre o FEM em MG citou o manual MG (oficial, proximidade 0,6844); a de erros comuns citou o blog (interno) e trechos oficiais; a do IBS no VL_DOC seguiu no Guia 3.2.4 (Cap. I, Seção 10). Suíte: 494 testes aprovados.

**Pendências**: importar a tabela de NCM no Neon; T019 a T021.

## Phase 5: Aceite

- [ ] T019 [M] Executar os [critérios de aceite](contracts/criterios-aceite.md) com evidências (URLs, respostas e trechos de log sem segredos)
- [ ] T020 [M] Testar o rollback (Instant Rollback do Vercel e restauração da branch do Neon) e registrar o tempo
- [ ] T021 [A] Registrar consumo e custos (OpenAI, quotas do Vercel, Atlas e Neon) e os riscos aceitos em `specs/002-publicacao-cloud/evidencias/`

**Ressalvas de T007/T008 (aprovadas em 2026-11-10)**: opções `Recuperacao:InstanciaUnica` (padrão `true`, comportamento local inalterado), `Recuperacao:LimiarMinutos` (10) e `Recuperacao:IntervaloVarreduraMinutos` (1). Na Api.Agente o limiar usa `AtualizadoEm` e, com várias instâncias, uma varredura periódica encerra execuções de instâncias que pararam. Na Api.Rag o limiar usa `IngeridoEm`, sem mudar o esquema; o reprocessamento de um documento antigo ainda pode ser retomado em paralelo por uma instância que inicie durante ele (risco aceito, demonstração). Testes: `StatusExecucaoTests`, `RecuperacaoVariasInstanciasTests` (MongoDB e PostgreSQL reais); suíte completa com 463 testes aprovados.

**T009 (2026-11-10)**: nenhuma mudança de código foi necessária; `LimiteVercelTests` nas duas APIs confirma `413` com `application/problem+json` acima de 4 MB. Suíte completa: 476 testes aprovados.

## Dependências

T001 bloqueia a Phase 4. T008 deve estar mergeada antes de T016/T017. T010 → T011 → T014. T012 → T013.
T016 antes de T017 (a Api.Agente precisa da URL da Api.Rag).
