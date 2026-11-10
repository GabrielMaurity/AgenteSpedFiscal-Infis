using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Rag.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class DocumentoArquivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_falha",
                table: "documento",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "documento_arquivo",
                columns: table => new
                {
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    tipo_conteudo = table.Column<string>(type: "text", nullable: false),
                    conteudo = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documento_arquivo", x => x.documento_id);
                    table.ForeignKey(
                        name: "FK_documento_arquivo_documento_documento_id",
                        column: x => x.documento_id,
                        principalTable: "documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documento_arquivo");

            migrationBuilder.DropColumn(
                name: "motivo_falha",
                table: "documento");
        }
    }
}
