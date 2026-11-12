# Research: Validação da EFD ICMS/IPI (SPED Fiscal)

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-29 (R-30 a R-36: 2026-11-10)

Cada item registra **Decisão**, **Justificativa** e **Alternativas consideradas**. Versões exatas de
pacotes são fixadas em `Directory.Packages.props` no início da implementação (última versão
estável compatível com .NET 10); este documento não as fixa (constitution, seção 3.1).

Itens marcados com **ADR** DEVEM ser registrados em `docs/adr/` antes da tarefa que os implementa
(constitution 6.3).

---

## R-01 — Plataforma e estilo de API

- **Decisão**: .NET 10 (LTS) com ASP.NET Core Minimal APIs, `Nullable` habilitado, analisadores
  .NET habilitados e `TreatWarningsAsErrors` nos projetos de produção. Solução em formato `.slnx`.
- **Justificativa**: exigido pela constitution (3.1) e pelo usuário. Minimal APIs reduzem
  cerimônia e combinam com a camada de endpoints fina exigida por P-08.
- **Alternativas**: Controllers MVC (mais código sem ganho para APIs pequenas).

## R-02 — Estrutura de projetos

- **Decisão**: dois serviços, cada um com quatro projetos de camada (`Endpoints` como host,
  `Application`, `Domain`, `Infrastructure`), mais uma biblioteca `AgenteSpedFiscal.Contracts.Mcp`
  com os DTOs das ferramentas MCP. Testes em projetos separados por serviço e tipo.
- **Justificativa**: projetos separados tornam a direção de dependência verificável em tempo de
  compilação (P-08: o domínio não referencia infraestrutura nem SDKs). A biblioteca de contratos
  MCP é permitida pela constitution 3.4 (contratos sem estado, sem acesso a dados) e evita que os
  dois lados divirjam no contrato.
- **Alternativas**: um projeto por serviço com pastas por camada — mais simples, mas a regra de
  dependência só seria verificável por teste de arquitetura; rejeitada porque P-08 é um gate de
  PR e a verificação por compilação é mais barata. Registrado em Complexity Tracking.

## R-03 — Leitura de arquivos de até 500 MB

- **Decisão**: upload em streaming para arquivo temporário (sem `IFormFile` em memória),
  processamento assíncrono e leitura **em fluxo, em uma única passagem**, linha a linha. Durante a
  passagem são mantidos apenas índices compactos (cadastros `0190`/`0200`/`0220`, contagens por
  tipo de registro, totais por documento `C100`, pilha de hierarquia e referências pendentes);
  regras que dependem de dados posteriores são resolvidas ao final da passagem. O arquivo
  temporário é apagado em bloco `finally` ao fim da execução, inclusive em falha (FR-082).
- **Justificativa**: memória limitada pelo tamanho dos cadastros, não do arquivo; atende SC-005
  (500 MB em até 15 min) sem infraestrutura de big data.
- **Alternativas**: carregar o arquivo inteiro (inviável para 500 MB); duas passagens (dobra o I/O
  sem necessidade); processamento distribuído (complexidade injustificada).

## R-04 — Execução assíncrona

- **Decisão**: `POST` de validação retorna `202 Accepted` com o identificador da execução; um
  `BackgroundService` consome uma fila em memória (`System.Threading.Channels`) com paralelismo
  configurável. O estado da execução é persistido no MongoDB a cada etapa. Execuções em andamento
  quando o serviço reinicia são marcadas como `falha` (categoria falha técnica), e o usuário
  reenvia o arquivo — coerente com o descarte do arquivo (FR-082).
- **Justificativa**: atende arquivos grandes sem bloquear a requisição; sem broker externo.
- **Alternativas**: RabbitMQ/Azure Service Bus (durabilidade não necessária, pois o arquivo não é
  retido); processamento síncrono (timeouts de HTTP para arquivos grandes).

## R-05 — Motor de validação orientado a leiaute

- **Decisão**: o leiaute de cada versão é um arquivo de dados versionado no repositório
  (`leiaute-efd-icms-ipi-<COD_VER>.json`), com blocos, registros, nível hierárquico, ocorrência
  (1:1, 1:N), registro pai, campos (número, nome, tipo `N`/`C`, tamanho, tamanho exato `*`,
  decimais, obrigatoriedade `O`/`OC`) e a localização no Guia (página/seção). Validadores
  estruturais genéricos leem essa definição. Regras de negócio são classes específicas, uma por
  regra do catálogo. O Guia 3.2.4 informa "Nível hierárquico" e "Ocorrência" por registro (p.ex.
  Cap. II, Seção 1), o que permite derivar a hierarquia sem inventá-la.
- **Justificativa**: regras estruturais ficam em dados revisáveis em PR com referência à fonte
  (P-01); novas versões de leiaute não exigem código novo.
- **Alternativas**: codificar cada registro em classes (milhares de linhas, difícil de revisar
  contra o Guia); validação por XSD (proibida para TXT, P-02).
- **Seleção da versão**: a Tabela Versão do Leiaute (NT 2026.001 v1.0, item 3.1.1) fica em dados
  versionados, com fonte. O `COD_VER` é validado contra o `DT_FIN` do `0000` (Guia 3.2.4). Os
  campos 020 vêm do Guia 3.2.4; o Anexo I da NT lista o que muda de 020 para 021.

## R-06 — Catálogo de Regras

- **Decisão**: catálogo versionado em arquivo de dados no repositório
  (`catalogo-regras-efd-icms-ipi.json`), validado por JSON Schema
  ([catalogo-regras.schema.json](contracts/catalogo-regras.schema.json)) no build e na
  inicialização. Uma regra com status `ativa` sem `fonte` completa (documento, versão,
  localização) falha o build. Regras `pendente_confirmacao` são carregadas, mas nunca executadas,
  e aparecem no resultado como "não realizadas" com o motivo.
- **Justificativa**: torna P-01 e FR-030/FR-031 verificáveis automaticamente em PR.
- **Alternativas**: catálogo em banco de dados (alterações sem revisão de PR; rejeitado).

## R-07 — Codificação do arquivo

- **Decisão**: a codificação aceita é ISO 8859-1 (Latin-1), exigida pela NT 2026.001, item 2.1.1, e
  fica nos dados de cada versão de leiaute. Toda linha termina em CR e LF (item 2.1.1, alínea g). O Guia 3.2.4 (Cap. II, Seção 3) define o conjunto de caracteres
  válidos para campos `C` (Tabela ASCII, exceto `|` e caracteres 00–31) e para campos `N`
  (algarismos e vírgula decimal); essas regras são catalogadas. Bytes inválidos para a codificação
  configurada geram erro estrutural com a linha.
- **Justificativa**: a fonte oficial define a codificação (P-01); o Guia não a nomeia.
- **Alternativas**: aceitar também UTF-8 (fora da norma; o PVA é a referência).

## R-08 — Volume de achados

- **Decisão**: para cada regra, o resultado guarda as primeiras N ocorrências detalhadas
  (configurável, inicial 1.000) e o total de ocorrências. O LLM recebe apenas um resumo agregado
  por regra e amostras.
- **Justificativa**: um arquivo corrompido pode gerar milhões de achados; o limite protege
  memória, armazenamento e custo de tokens, sem esconder a contagem real.
- **Alternativas**: gravar todos os achados (resultado inutilizável e caro).

## R-09 — Integração com LLM (OpenAI + Semantic Kernel)

- **Decisão**: Semantic Kernel com o conector OpenAI. Saída estruturada por JSON Schema estrito
  (`response_format` do tipo `json_schema` com `strict: true`) gerado a partir do contrato
  versionado [llm-analise.schema.json](contracts/llm-analise.schema.json). Modelo, temperatura,
  limite de tokens e timeout vêm de configuração. Retentativas de resposta inválida: no máximo 2
  (configurável). Esgotadas, a análise da IA é marcada como falha técnica e o relatório
  determinístico é entregue mesmo assim (FR-084).
- **Escolha do modelo (D-02)**: o modelo de análise é escolhido pelo conjunto de avaliação
  (R-17) entre os modelos da OpenAI com suporte a saída estruturada estrita e a function calling;
  a escolha e a tabela de resultados vão para **ADR-0003**. O código não depende do nome do
  modelo.
- **Justificativa**: SK é exigido pela constitution; schema estrito reduz respostas inválidas.
- **Alternativas**: SDK OpenAI direto (perde o pipeline de filtros de função usado em R-11).

## R-10 — O que o LLM pode produzir

- **Decisão**: o contrato do LLM contém apenas: explicações vinculadas a `achadoId`s existentes,
  indícios (`categoria = indicio_ia`, severidade nunca impeditiva), limitações e referências
  (`trechoId`s). O LLM **não** produz achados estruturais ou de negócio, status, identificadores,
  consumo ou tempos (P-03, P-12). A validação semântica rejeita: `achadoId` inexistente,
  `trechoId` não recuperado na execução, severidade impeditiva em indício, e qualquer tentativa de
  alterar achado determinístico.
- **Justificativa**: implementa P-02, P-12, P-13 e FR-047 por construção do contrato.
- **Alternativas**: deixar o LLM reescrever o relatório inteiro (violaria P-02).

## R-11 — Ferramentas e autorização (P-15)

- **Decisão**: ferramentas do Semantic Kernel na `Api.Agente`, todas somente leitura:
  `consultar_manual` (encaminha para a ferramenta MCP `buscar_trechos`), `obter_trecho`,
  `consultar_regra` (catálogo) e `obter_achados` (resultado determinístico da execução corrente).
  Um `IFunctionInvocationFilter` verifica, antes de cada chamada, a lista de ferramentas
  permitidas para o tipo de execução e as permissões do usuário, valida argumentos, aplica
  timeout e registra a chamada. Chamadas negadas retornam erro ao modelo e são registradas.
  Número máximo de chamadas de ferramenta por execução configurável (inicial 8).
- **Justificativa**: a escolha do LLM não autoriza a execução; o filtro decide.
- **Alternativas**: autorização dentro de cada função (duplicada e fácil de esquecer).

## R-12 — MCP entre Api.Agente e Api.RAG

- **Decisão**: SDK oficial C# do Model Context Protocol (`ModelContextProtocol` e
  `ModelContextProtocol.AspNetCore`) com transporte **Streamable HTTP**. A `Api.RAG` expõe as
  ferramentas descritas em [mcp-api-rag.md](contracts/mcp-api-rag.md); a `Api.Agente` é cliente.
  Autenticação por token Bearer de serviço (R-15). O identificador de correlação é propagado em
  cabeçalho e nos argumentos. **ADR-0005**.
- **Justificativa**: exigido pela constitution (P-07); Streamable HTTP é o transporte remoto
  padrão da especificação MCP.
- **Alternativas**: stdio (só local); REST direto (proibido para uso funcional, P-07).

## R-13 — Busca no RAG (D-06)

- **Decisão**: busca **híbrida**: similaridade vetorial (pgvector, distância de cosseno, índice
  HNSW) combinada com busca textual do PostgreSQL (`tsvector`, dicionário `portuguese` mais
  correspondência exata de códigos de registro e campo, como `C190` e `VL_OPR`), fundidas por
  Reciprocal Rank Fusion. Filtro obrigatório por tipo de documento, versão e status ativo antes do
  ranqueamento. **ADR-0002**.
- **Justificativa**: perguntas fiscais citam códigos exatos que a busca vetorial pura recupera
  mal; o filtro por versão implementa P-09.
- **Parâmetros iniciais para avaliação (não aprovados; P-10)**: chunks por unidade estrutural do
  Guia (seção/registro, com a tabela de campos do registro inteira em um chunk), limite inicial de
  ~800 tokens com sobreposição de 1 parágrafo para textos longos; `topK` inicial 8; sem limiar
  fixo até a avaliação. Valores finais registrados após R-17.
- **Alternativas**: só vetorial (perde códigos exatos); só textual (perde paráfrases).

## R-14 — Embeddings (D-02)

- **Decisão**: modelo de embeddings da OpenAI configurável; candidato inicial
  `text-embedding-3-small` (1.536 dimensões), compatível com índice HNSW do tipo `vector` do
  pgvector. Cada chunk guarda o modelo e a dimensão; a busca só compara vetores do mesmo
  modelo/dimensão; trocar o modelo dispara reprocessamento. **ADR-0002**.
- **Justificativa**: dimensão dentro do limite de indexação do tipo `vector`; custo baixo; P-10.
- **Alternativas**: `text-embedding-3-large` (3.072 dimensões, exige `halfvec` para HNSW; adotar
  se a avaliação mostrar ganho relevante).

## R-15 — Autenticação e autorização (D-07)

- **Decisão**: JWT Bearer (OIDC) em REST, MCP e A2A. Claims obrigatórias: `sub` (usuário),
  `org_id` (organização) e papéis `analista`, `admin_base` e `agente_externo`. Entre serviços:
  token de cliente (client credentials) com audiência `api-rag`. Em desenvolvimento:
  `dotnet user-jwts`. O provedor OIDC de produção fica para **ADR-0004** (não bloqueia o
  desenvolvimento, pois o código depende só de JWT padrão). Endpoints `/health/*` sem
  autenticação, sem dados (exceção justificada, constitution 5.1).
- **Justificativa**: padrão de mercado; isolamento por organização em todas as consultas
  (FR-080).
- **Alternativas**: API keys (sem identidade de usuário nem papéis); cookies (não serve a agentes).

## R-16 — A2A

- **Decisão**: SDK .NET do protocolo A2A (`A2A` / `A2A.AspNetCore`), com Agent Card publicado em
  `/.well-known/agent-card.json` e duas skills: `validar-efd-icms-ipi` e `consultar-efd-icms-ipi`
  ([a2a-agent-card.json](contracts/a2a-agent-card.json)). O handler A2A chama os mesmos casos de
  uso da API REST e devolve o mesmo envelope (FR-081, P-20). **ADR-0006**. O SDK ainda não tem versão
  estável (`1.0.0-preview4`), risco registrado no ADR.
- **Justificativa**: exigido pela constitution; mesma camada de aplicação garante equivalência.
- **Alternativas**: implementar o protocolo manualmente (risco de incompatibilidade).

## R-17 — Avaliação de RAG e LLM

- **Decisão**: projeto `AgenteSpedFiscal.Evals` com conjuntos versionados em `evals/`:
  (a) perguntas com trechos esperados do Guia 3.2.4 (recall@k, MRR); (b) achados com explicações
  esperadas (avaliação por rubrica); (c) casos de injeção de prompt; (d) perguntas sem resposta.
  (e) perguntas cuja resposta está só em material interno ou mistura oficial e interno, para
  medir se a classificação é sinalizada corretamente (R-27).
  Executado sob demanda e em mudanças de prompt, modelo ou parâmetros (constitution 5.2); fora da
  suíte de PR (P-22).
- **Justificativa**: SC-006, SC-007 e SC-008 exigem medição; P-10 exige avaliação antes de fixar
  parâmetros.
- **Alternativas**: avaliação manual ad hoc (não reprodutível).

## R-18 — Extração de PDF e ingestão

- **Decisão**: extração com UglyToad.PdfPig preservando número de página e reconstruindo linhas
  por posição (para manter as tabelas de campos). Segmentação pela estrutura do Guia (Capítulo →
  Seção → Registro), detectada pelos títulos (`REGISTRO XXXX:`), com cabeçalho e rodapé removidos.
  Deduplicação por SHA-256 do arquivo (documento) e do texto normalizado (chunk). Ingestão
  assíncrona com status (`recebido`, `processando`, `ativo`, `falha`, `inativo`). Material interno
  sem estrutura de capítulos é segmentado por títulos e parágrafos, com o mesmo limite de tamanho.
- **Justificativa**: citações precisam de página e registro (FR-051); a extração em texto puro
  desalinha as tabelas, como visto no Guia 3.2.4.
- **Alternativas**: `pdftotext` externo (dependência nativa no container); serviços de OCR na
  nuvem (envio de documento a terceiros sem necessidade, pois o PDF tem texto).

## R-19 — Persistência

- **Decisão**:
  - **MongoDB** (`Api.Agente`, ADR-0001 já decidido): coleções `execucoes` (estado e resultado,
    índice TTL de 90 dias em `expiraEm`), `conversas` (mensagens, índice TTL configurável, inicial
    90 dias, com limite de mensagens enviadas ao modelo, inicial 20).
  - **PostgreSQL `agente`** (`Api.Agente`): `consumo_ia`, `tabela_precos`, `tabela_referencia` e
    `ncm`.
  - **PostgreSQL `rag` + pgvector** (`Api.RAG`): `documento`, `trecho`, `consumo_embeddings`.
  - Bancos lógicos separados com usuários distintos; cada serviço só recebe a própria connection
    string (D-17, **ADR-0007**). Em produção PODEM ser instâncias separadas.
  - EF Core 10 com Npgsql (e `Pgvector.EntityFrameworkCore` no RAG); migrações versionadas.
- **Justificativa**: resultados são documentos com TTL natural (Mongo); consumo e tabelas são
  relacionais (constitution exige PostgreSQL para consumo); P-07 por credenciais separadas.
- **Alternativas**: um único banco compartilhado (viola P-07); resultados em PostgreSQL (TTL
  manual com job).

## R-20 — Tabelas de referência (NCM)

- **Decisão**: as tabelas de referência pertencem à **Api.Agente** (dados de validação, não
  conhecimento textual), importadas por endpoint administrativo com origem, versão e vigência
  (FR-063). Consultas em lote em memória durante a validação (conjunto de NCMs vigentes do
  período carregado uma vez por execução).
- **Justificativa**: validar milhares de itens por chamadas MCP seria lento e acoplaria a validação
  determinística à disponibilidade do RAG; a constitution atribui à Api.RAG a base de
  conhecimento, não dados de validação.
- **Alternativas**: tabela no RAG via ferramenta MCP `verificar_ncm` (latência e dependência
  desnecessárias).

## R-21 — Consumo e custos (D-11, D-12)

- **Decisão**: cada serviço registra o próprio consumo no seu PostgreSQL, com o mesmo
  `correlacaoId`. Tokens vêm do `usage` da resposta do provedor (`origem = informado`); custo é
  calculado com a tabela de preços versionada (`origem = estimado`, com `tabelaPrecosId`). Sem
  `usage`, tokens ficam nulos com `status_consumo = desconhecido` (nunca zero). Retentativas geram
  registros próprios. A tabela de preços é um arquivo versionado no repositório com data e URL de
  referência, importado na inicialização. Relatório consolidado entre serviços fica para feature
  futura.
- **Justificativa**: P-17 e P-07.
- **Alternativas**: um serviço gravar o consumo do outro (acoplamento de dados).

## R-22 — Resiliência (D-13)

- **Decisão**: `Microsoft.Extensions.Http.Resilience` (pipeline padrão: timeout por tentativa,
  retentativa com backoff exponencial e jitter, circuit breaker) em OpenAI e MCP. Valores iniciais:
  timeout 60 s (LLM), 10 s (MCP), 2 retentativas para erros transitórios; todos configuráveis.
- **Justificativa**: constitution 5.2.
- **Alternativas**: Polly manual (mais código).

## R-23 — Observabilidade (D-18)

- **Decisão**: OpenTelemetry (traces, métricas e logs) com exportação OTLP; em desenvolvimento, o
  Aspire Dashboard standalone no docker compose. Logs estruturados com `execucaoId` e
  `correlacaoId` em todos os escopos; conteúdo de linhas do arquivo, CNPJ/CPF e segredos são
  mascarados (P-18).
- **Justificativa**: padrão aberto, sem dependência de fornecedor.
- **Alternativas**: Serilog com sinks proprietários.

## R-24 — Testes (D-19)

- **Decisão**: xUnit v3; Testcontainers para PostgreSQL/pgvector e MongoDB em testes de
  integração; `WebApplicationFactory` para testes de API; validação de contratos com JSON Schema
  (JsonSchema.Net); LLM substituído por implementação falsa de `IChatCompletionService` na suíte
  determinística; testes de arquitetura (NetArchTest) para a direção de dependências.
  Arquivos EFD de referência sintéticos em `tests/fixtures/efd/` com gabarito de achados.
- **Justificativa**: P-22; suíte de PR sem chamadas reais ao provedor.
- **Alternativas**: NUnit/MSTest (equivalentes; xUnit é o padrão mais comum em .NET).

## R-25 — Docker

- **Decisão**: Dockerfile multi-stage por API (SDK .NET 10 para build; imagem `aspnet:10.0`
  chiseled para execução, usuário não root). `docker-compose.yml` com `api-agente`, `api-rag`,
  `postgres` (imagem `pgvector/pgvector` com PostgreSQL 17), `mongo` (MongoDB 8) e
  `aspire-dashboard`. Script de inicialização do PostgreSQL cria os bancos `agente` e `rag`, os
  usuários e a extensão `vector` só no banco `rag`. Segredos via arquivo `.env` (não versionado) e
  `.env.example` versionado.
- **Justificativa**: pedido do usuário; ambiente reprodutível para testes e quickstart.
- **Alternativas**: .NET Aspire AppHost (adicionaria orquestração além do pedido; PODE ser
  avaliado depois).

## R-26 — Limitação de requisições

- **Decisão**: rate limiter do ASP.NET Core por `org_id` (janela fixa, configurável) em
  validação e conversa; limite de execuções simultâneas por organização.
- **Justificativa**: protege custos de IA e a fila de processamento.
- **Alternativas**: sem limites (risco de custo descontrolado).

## R-27 — Classificação oficial/interno na base de informação (FR-051, FR-060)

- **Decisão**: `classificacao` (`oficial` \| `interno`) obrigatória na ingestão, gravada no
  documento e replicada no trecho para filtrar sem junção. A ferramenta MCP `buscar_trechos`
  aceita `classificacoes` (padrão: ambas) e devolve a classificação de cada trecho. A Api.Agente
  calcula `fundamentadaEmFonteOficial` a partir das classificações dos trechos efetivamente
  citados (metadado do sistema, P-12) e, quando só há trechos internos, acrescenta a limitação
  "sem fundamento em fonte oficial recuperada". O prompt instrui o modelo a priorizar trechos
  oficiais e a nunca apresentar trecho interno como norma. Sem reforço de ranqueamento por
  classificação na primeira versão; a avaliação (R-17) inclui perguntas com trechos dos dois tipos
  para decidir se ele é necessário.
- **Regra do catálogo**: o catálogo mantém uma lista `fontesOficiais` (documento e versão); a
  `fonte` de uma regra ativa DEVE pertencer a essa lista, verificada no build. Assim, material
  interno não vira fonte de regra sem que a Api.Agente acesse dados da Api.RAG (P-07).
- **Justificativa**: preserva a P-01 e dá aos analistas acesso ao conhecimento interno com
  origem explícita.
- **Alternativas**: bases separadas por classificação (duplica ingestão, busca e manutenção);
  inferir a classificação pelo tipo de documento (tipos ambíguos, como legislação comentada).

## R-28 — Exportação do resultado em CSV (FR-048)

- **Decisão**: `GET /api/v1/validacoes/{id}/exportacao.csv` gera o CSV em streaming a partir do
  resultado armazenado (sem montar o arquivo em memória). Formato: UTF-8 com BOM, separador `;`
  e vírgula decimal (padrão do Excel em pt-BR), aspas conforme RFC 4180. Linhas iniciais com `#`
  trazem os metadados da execução; em seguida, cabeçalho e uma linha por achado detalhado; ao
  final, uma seção de totais por regra quando o total excede as ocorrências detalhadas (R-08).
  Células que começam com `=`, `+`, `-`, `@`, tabulação ou retorno de carro são prefixadas com
  apóstrofo, evitando injeção de fórmulas. Contrato em
  [exportacao-csv.md](contracts/exportacao-csv.md).
- **Justificativa**: o CSV é lido do mesmo resultado da API, o que garante o SC-003 (mesmos
  achados); o streaming suporta resultados grandes.
- **Alternativas**: XLSX (biblioteca extra e geração em memória); separador vírgula (abre
  desalinhado no Excel pt-BR).

## R-29 — Perguntas sobre arquivo já descartado

- **Decisão**: a conversa só acessa o resultado armazenado da execução pela ferramenta
  `obter_achados`; não existe ferramenta de leitura do arquivo. O prompt de sistema declara que o
  arquivo foi descartado. Quando a pergunta exige dado ausente dos achados e da identificação, a
  resposta é `fundamentada = false`, com a limitação e a orientação de reenviar o arquivo.
- **Justificativa**: coerente com FR-082 e com o novo caso de borda da spec; impede respostas
  inventadas sobre o conteúdo do arquivo (P-13).
- **Alternativas**: reter o arquivo para conversas (contraria a retenção decidida).

## R-30 — Itens fora do alcance desta validação (clarify de 2026-11-05)

- **Decisão**: o resultado ganha a lista `foraDoAlcance[]` ({referencia, categoria, motivo}),
  separada de `validacoesNaoRealizadas[]`. Vão para ela as regras `pendente_confirmacao`
  aplicáveis ao período e as regras ativas cujo `dependeDe` não pode ser atendido pelo arquivo
  (`xml:documento`). Em `validacoesNaoRealizadas[]` ficam só as dependências que podem voltar
  (`tabela:ncm`, base de informação, IA) e as falhas técnicas. `concluida` passa a exigir
  `validacoesNaoRealizadas` vazia e análise da IA aceita; `foraDoAlcance` não entra nessa conta
  nem em `revisaoHumanaNecessaria`. O `avisoPva` continua obrigatório (FR-044 a FR-046).
- **Contrato**: envelope 1.1.0 com `foraDoAlcance` opcional e `versaoContrato` aceitando `1.0.0`
  durante a transição; a tarefa que implementar torna o campo obrigatório e retira o `1.0.0`. O
  CSV ganha linhas `# foraDoAlcance;<referencia>;<categoria>;<motivo>` (contrato CSV 1.1.0).
- **Justificativa**: sem a separação, `EFD-NEG-008` e `EFD-NEG-009` impediam `concluida` em
  praticamente todo arquivo, e o status deixava de distinguir dependência fora do ar de limite
  permanente. Nada é omitido: os itens continuam em todo resultado (P-04).
- **Alternativas**: manter tudo em `validacoesNaoRealizadas` (status sem utilidade); omitir as
  regras pendentes do resultado (esconde limite da validação, contraria P-04).

## R-31 — Lacuna de numeração condicional à fonte oficial (FR-024)

- **Decisão**: `EFD-NEG-008` fica no catálogo como `pendente_confirmacao`, com `origemCandidata`,
  e o motor só instancia a regra quando ela estiver ativa. A lógica cobre o `C100` de emissão
  própria, por modelo e série, contando cancelados, denegados e inutilizados informados.
- **Justificativa**: o Guia 3.2.4 e a NT 2026.001 não trazem essa regra, e o Guia dispensa
  informar denegados e inutilizados desde 12/2021 (registro C100, p. 63): executar sem fonte
  violaria P-01 e geraria alertas indevidos.
- **Alternativas**: remover a regra (perde o trabalho pronto); executar como aviso interno (exige
  desvio de P-01).

## R-32 — Severidade das regras de negócio

- **Decisão**: `impeditiva` quando o Guia traz validação explícita do campo ou registro;
  `advertencia` quando a regra é indireta ou o Guia tem textos conflitantes (`EFD-NEG-003`, `005`
  e `006`). A comparação com o PVA (T108, SC-002) confirma ou corrige cada severidade no catálogo.
- **Justificativa**: o Guia não diz quais validações o PVA trata como erro ou advertência; uma
  impeditiva indevida bloqueia o analista sem motivo.
- **Alternativas**: tudo impeditivo (falsos bloqueios); tudo advertência (perde o sinal dos erros
  que impedem a transmissão).

## R-33 — Exceções de obrigatoriedade por situação do documento

- **Decisão**: o leiaute guarda, por registro, `excecoesObrigatoriedade[]` ({situacoes,
  camposObrigatorios, fonte}) para os casos em que o Guia exige só parte dos campos conforme o
  `COD_SIT`: `C100`, `C500`, `D100`, `D400`, `D500`, `D700` e `1500`. O extrator gera esses dados a
  partir de uma tabela revisada contra o Guia; o validador de campos aplica a exceção da situação
  informada na linha.
- **Justificativa**: sem isso, documento cancelado gerava falso erro de campo obrigatório vazio;
  dados com fonte mantêm a regra fora do código (P-01, R-05).
- **Alternativas**: tratar `C100` no código (não cobre os demais registros e esconde a fonte).

## R-34 — Formato da tabela de NCM importada (FR-063)

- **Decisão**: o importador aceita JSON com a lista `Nomenclaturas` (campos `Codigo`,
  `Descricao`, `Data_Inicio`, `Data_Fim`) e texto delimitado por `;`, `|` ou tabulação (código,
  descrição e, opcionalmente, início e fim da vigência), em UTF-8 ou ISO 8859-1. Só códigos de 8
  dígitos são gravados; `31/12/9999` vale como sem fim de vigência.
- **Atualização (2026-11-10)**: a tabela oficial recebida é uma planilha `.xlsx` com títulos antes do cabeçalho e as colunas `Código`, `Descrição`, `Data Início`, `Data Fim`, `Ato Legal Início`, `Número` e `Ano`; o leitor precisa aceitar esse formato (T120).
- **Pendência anterior**: o formato ainda não foi conferido em arquivo oficial. Ao obter a tabela (tabelas
  do PVA ou publicação oficial de NCM), ajustar o leitor e registrar a fonte usada.
- **Alternativas**: um único formato fixo (depende de uma fonte ainda não obtida).

## R-35 — Registros obrigatórios ausentes e obrigatoriedade por perfil

- **Situação**: em aberto. A pergunta do clarify de 2026-11-05 ficou sem resposta. Hoje um
  arquivo sem `E100`, `E110` ou `1010` passa sem achado, e o campo `obrigatoriedadePorPerfil` do
  data-model não foi implementado.
- **Opções levantadas**: (A) achado impeditivo conforme o perfil, com base na tabela do Guia
  (Cap. III, Seção 5), carregada como dados no leiaute, opção recomendada; (B) só os registros
  obrigatórios para todos os contribuintes; (C) deixar na lista `foraDoAlcance`.
- **Próximo passo**: decidir em `/speckit-clarify` antes de `/speckit-tasks`; a decisão muda o
  leiaute e o extrator.

## R-36 — Documentação interativa da API (Swagger)

- **Decisão (revista em 2026-11-10, a pedido do usuário)**: adotada. Cada API gera o documento
  com `Microsoft.AspNetCore.OpenApi` em `/openapi/v1.json` e exibe a tela do Swagger
  (`Swashbuckle.AspNetCore.SwaggerUI`) em `/swagger`, só no ambiente `Development`, com o
  esquema Bearer para colar o token do `dotnet user-jwts`. Os dois caminhos são anônimos e ficam
  antes da autenticação, porque a política padrão exige token em toda rota.
- **Consequência**: os contratos de referência continuam nos arquivos OpenAPI escritos à mão em
  `contracts/`. Ainda falta um teste que compare o documento gerado com o contrato.

## R-37 — Execução local pelo Visual Studio: configuração e autenticação (2026-11-10)

- **Decisão (pedido do usuário)**: as APIs podem rodar pelo Visual Studio, com os bancos no Docker
  (`docker compose up -d postgres mongo aspire-dashboard`). O `docker-compose.override.yml` publica
  o Postgres em `127.0.0.1:5432`, o Mongo em `127.0.0.1:27018` (a 27017 costuma estar ocupada por um
  MongoDB instalado na máquina) e o OTLP do Aspire em `127.0.0.1:18889`.
- **Segredos locais**: ficam nos **User Secrets** do .NET (Manage User Secrets), um arquivo por
  projeto em `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, fora do repositório
  (P-19). O `appsettings.Development.json` foi descartado para segredos: embora esteja no
  `.gitignore`, ele não é excluído pelo `.dockerignore` e o `dotnet publish` o copiaria para as
  imagens Docker e do Vercel.
- **Autenticação**: com `Autenticacao:Desabilitada = true`, ligada no perfil `http` dos
  `launchSettings.json`, toda requisição entra como um usuário local (`usuario-local`,
  `org-local`) com todos os papéis; as políticas continuam no código. A chave só vale no ambiente
  `Development`; em outro, a API não inicia. Docker, testes e nuvem seguem com JWT (FR-080). O token
  de serviço do MCP (`McpRag:TokenServico`) é opcional nesse modo.
- **Alternativas**: `appsettings.Development.json` (risco de ir para a imagem, acima); tokens do
  `dotnet user-jwts` também no Visual Studio (adiado a pedido do usuário; continua documentado no §2
  do quickstart).

## R-38 — Proximidade da resposta com a base (2026-11-10)

- **Decisão (pedido do usuário)**: a resposta da conversa ganha `proximidade`, de 0 a 1, calculada
  pelo sistema e nunca pelo LLM (P-12). A `buscar_trechos` passa a devolver `similaridade` por
  trecho: 1 − distância de cosseno no pgvector entre o embedding da consulta e o do trecho, limitada
  a [0, 1]. A Api.Agente usa a maior similaridade entre os trechos **citados** na resposta; sem
  citação, entre os trechos **recuperados** para a pergunta; 0 sem trechos, com a base
  indisponível ou em `obter_trecho` (que não tem consulta).
- **Por que não a `pontuacao`**: a nota da fusão RRF só ordena e não fica entre 0 e 1.
- **Limite**: mede a proximidade semântica entre pergunta e trecho, não a correção da resposta.
  O valor depende do modelo de embeddings; não há limiar de corte até os evals (R-17).
- **Correção junto**: a limitação de arquivo descartado (R-29) só entra em conversa vinculada a uma
  execução; sem vínculo, `dependeDoArquivo` do modelo é ignorado e vale a limitação de falta de
  evidência (FR-052).

## R-39 — Outros documentos oficiais nas respostas e teto de tamanho dos trechos (2026-11-12)

- **Decisão (pedido do usuário)**: além do Guia Prático (`tipoDocumento` principal, filtrado por versão
  ou período), a busca inclui documentos oficiais de outros tipos (`nota_tecnica_leiaute`, `ato_cotepe`,
  `legislacao`, `outro_oficial`), filtrados só pela vigência no período. O contrato MCP ganhou o
  parâmetro opcional `tiposDocumentoAdicionais` (mudança aditiva); a Api.Agente envia a lista de
  `BaseInformacao:TiposOficiaisAdicionais`. Primeiro caso: Manual Consolidado de Escrituração do ICMS de
  Minas Gerais (SEF/MG) v.2025.02, ingerido como `oficial`/`outro_oficial`.
- **Documentos estaduais**: valem só para a UF de origem; o prompt instrui o modelo a dizer a UF ao usar
  esses trechos. Ainda não há filtro por UF na busca; a avaliação (R-17) deve medir se respostas para
  outras UFs passam a citar normas estaduais indevidas.
- **Teto dos trechos**: o Manual MG gerou trechos de até cerca de 14.700 tokens, acima do limite do
  modelo de embeddings (8.191), e a OpenAI recusou a ingestão com 400. `ParametrosSegmentacao.TetoTokens`
  (padrão 4.000, estimado) divide qualquer trecho maior por linhas, mantendo localização e páginas. O
  maior trecho do Guia 3.2.4 tem cerca de 3.844 tokens, então o Guia não muda (723 trechos).
- **Material de terceiros**: o PDF de erros comuns do blog TecnoSpeed entrou como `interno`
  (`material_interno`), porque não é norma; respostas que o usam continuam sinalizadas.
