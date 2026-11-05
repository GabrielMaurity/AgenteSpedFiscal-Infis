using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Agente.Infrastructure.Persistencia.Postgres.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tabela_precos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provedor = table.Column<string>(type: "text", nullable: false),
                    modelo = table.Column<string>(type: "text", nullable: false),
                    preco_entrada_por_milhao = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    preco_saida_por_milhao = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    moeda = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    referencia = table.Column<string>(type: "text", nullable: false),
                    versao_arquivo = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tabela_precos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tabela_referencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    origem = table.Column<string>(type: "text", nullable: false),
                    versao = table.Column<string>(type: "text", nullable: false),
                    vigencia_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    vigencia_fim = table.Column<DateOnly>(type: "date", nullable: true),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    importado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    importado_por = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tabela_referencia", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consumo_ia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    execucao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlacao_id = table.Column<string>(type: "text", nullable: false),
                    org_id = table.Column<string>(type: "text", nullable: false),
                    provedor = table.Column<string>(type: "text", nullable: false),
                    modelo = table.Column<string>(type: "text", nullable: false),
                    operacao = table.Column<string>(type: "text", nullable: false),
                    tentativa = table.Column<int>(type: "integer", nullable: false),
                    tokens_entrada = table.Column<int>(type: "integer", nullable: true),
                    tokens_saida = table.Column<int>(type: "integer", nullable: true),
                    origem_tokens = table.Column<string>(type: "text", nullable: false),
                    custo_estimado = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    moeda = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    tabela_precos_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duracao_ms = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumo_ia", x => x.id);
                    table.CheckConstraint("ck_consumo_ia_origem_tokens", "origem_tokens IN ('informado', 'desconhecido')");
                    table.ForeignKey(
                        name: "FK_consumo_ia_tabela_precos_tabela_precos_id",
                        column: x => x.tabela_precos_id,
                        principalTable: "tabela_precos",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "ncm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tabela_referencia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: false),
                    vigencia_inicio = table.Column<DateOnly>(type: "date", nullable: true),
                    vigencia_fim = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ncm", x => x.id);
                    table.ForeignKey(
                        name: "FK_ncm_tabela_referencia_tabela_referencia_id",
                        column: x => x.tabela_referencia_id,
                        principalTable: "tabela_referencia",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consumo_ia_execucao_id",
                table: "consumo_ia",
                column: "execucao_id");

            migrationBuilder.CreateIndex(
                name: "IX_consumo_ia_org_id_inicio",
                table: "consumo_ia",
                columns: new[] { "org_id", "inicio" });

            migrationBuilder.CreateIndex(
                name: "IX_consumo_ia_tabela_precos_id",
                table: "consumo_ia",
                column: "tabela_precos_id");

            migrationBuilder.CreateIndex(
                name: "IX_ncm_tabela_referencia_id_codigo",
                table: "ncm",
                columns: new[] { "tabela_referencia_id", "codigo" });

            migrationBuilder.CreateIndex(
                name: "IX_tabela_precos_provedor_modelo_vigente_desde",
                table: "tabela_precos",
                columns: new[] { "provedor", "modelo", "vigente_desde" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tabela_referencia_tipo_versao",
                table: "tabela_referencia",
                columns: new[] { "tipo", "versao" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumo_ia");

            migrationBuilder.DropTable(
                name: "ncm");

            migrationBuilder.DropTable(
                name: "tabela_precos");

            migrationBuilder.DropTable(
                name: "tabela_referencia");
        }
    }
}
