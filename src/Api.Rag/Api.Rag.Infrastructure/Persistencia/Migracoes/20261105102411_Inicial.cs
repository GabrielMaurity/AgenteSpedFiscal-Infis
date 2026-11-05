using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace Api.Rag.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "documento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    classificacao = table.Column<string>(type: "text", nullable: false),
                    tipo_documento = table.Column<string>(type: "text", nullable: false),
                    titulo = table.Column<string>(type: "text", nullable: false),
                    origem = table.Column<string>(type: "text", nullable: false),
                    versao = table.Column<string>(type: "text", nullable: false),
                    vigencia_inicio = table.Column<DateOnly>(type: "date", nullable: true),
                    vigencia_fim = table.Column<DateOnly>(type: "date", nullable: true),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    modelo_embedding = table.Column<string>(type: "text", nullable: true),
                    dimensao = table.Column<int>(type: "integer", nullable: true),
                    ingerido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ingerido_por = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documento", x => x.id);
                    table.CheckConstraint("ck_documento_classificacao", "classificacao IN ('oficial', 'interno')");
                    table.CheckConstraint("ck_documento_status", "status IN ('recebido', 'processando', 'ativo', 'falha', 'inativo')");
                });

            migrationBuilder.CreateTable(
                name: "consumo_embeddings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlacao_id = table.Column<string>(type: "text", nullable: false),
                    provedor = table.Column<string>(type: "text", nullable: false),
                    modelo = table.Column<string>(type: "text", nullable: false),
                    operacao = table.Column<string>(type: "text", nullable: false),
                    tentativa = table.Column<int>(type: "integer", nullable: false),
                    tokens_entrada = table.Column<int>(type: "integer", nullable: true),
                    origem_tokens = table.Column<string>(type: "text", nullable: false),
                    custo_estimado = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    moeda = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    referencia_preco = table.Column<string>(type: "text", nullable: true),
                    inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duracao_ms = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumo_embeddings", x => x.id);
                    table.CheckConstraint("ck_consumo_embeddings_origem_tokens", "origem_tokens IN ('informado', 'desconhecido')");
                    table.ForeignKey(
                        name: "FK_consumo_embeddings_documento_documento_id",
                        column: x => x.documento_id,
                        principalTable: "documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "trecho",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    classificacao = table.Column<string>(type: "text", nullable: false),
                    localizacao = table.Column<string>(type: "jsonb", nullable: false),
                    texto = table.Column<string>(type: "text", nullable: false),
                    texto_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    tsv = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('portuguese', texto)", stored: true),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trecho", x => x.id);
                    table.CheckConstraint("ck_trecho_classificacao", "classificacao IN ('oficial', 'interno')");
                    table.ForeignKey(
                        name: "FK_trecho_documento_documento_id",
                        column: x => x.documento_id,
                        principalTable: "documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consumo_embeddings_correlacao_id",
                table: "consumo_embeddings",
                column: "correlacao_id");

            migrationBuilder.CreateIndex(
                name: "IX_consumo_embeddings_documento_id",
                table: "consumo_embeddings",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "IX_documento_sha256",
                table: "documento",
                column: "sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documento_tipo_documento_versao",
                table: "documento",
                columns: new[] { "tipo_documento", "versao" });

            migrationBuilder.CreateIndex(
                name: "IX_trecho_classificacao",
                table: "trecho",
                column: "classificacao");

            migrationBuilder.CreateIndex(
                name: "IX_trecho_documento_id_ordem",
                table: "trecho",
                columns: new[] { "documento_id", "ordem" });

            migrationBuilder.CreateIndex(
                name: "IX_trecho_documento_id_texto_hash",
                table: "trecho",
                columns: new[] { "documento_id", "texto_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trecho_embedding",
                table: "trecho",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_trecho_tsv",
                table: "trecho",
                column: "tsv")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumo_embeddings");

            migrationBuilder.DropTable(
                name: "trecho");

            migrationBuilder.DropTable(
                name: "documento");
        }
    }
}
