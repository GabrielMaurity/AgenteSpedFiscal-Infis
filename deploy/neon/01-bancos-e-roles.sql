-- Equivalente ao docker/postgres/init/01-init.sh para o Neon (plano Free).
-- Um banco e um usuário por serviço (constitution P-07, ADR-0007); a extensão vector só no banco rag.
--
-- Executar com psql na conexão DIRETA (sem "-pooler" no host), como o usuário dono do projeto
-- (neondb_owner), passando as senhas como variáveis; nada de senha neste arquivo nem no histórico:
--
--   psql "postgresql://neondb_owner@<host-direto>/neondb?sslmode=require" \
--     -v agente_senha="$POSTGRES_AGENTE_PASSWORD" -v rag_senha="$POSTGRES_RAG_PASSWORD" \
--     -f deploy/neon/01-bancos-e-roles.sql
--
-- Revisar antes de executar. Rodar de novo falha em CREATE ROLE/DATABASE já existentes, o que é esperado.

\set ON_ERROR_STOP on

CREATE ROLE agente WITH LOGIN PASSWORD :'agente_senha';
CREATE ROLE rag WITH LOGIN PASSWORD :'rag_senha';

-- O dono do projeto precisa pertencer aos papéis para criar bancos em nome deles.
GRANT agente TO neondb_owner;
GRANT rag TO neondb_owner;

CREATE DATABASE agente OWNER agente;
CREATE DATABASE rag OWNER rag;
REVOKE ALL ON DATABASE agente FROM PUBLIC;
REVOKE ALL ON DATABASE rag FROM PUBLIC;

\connect rag
CREATE EXTENSION IF NOT EXISTS vector;
SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';
