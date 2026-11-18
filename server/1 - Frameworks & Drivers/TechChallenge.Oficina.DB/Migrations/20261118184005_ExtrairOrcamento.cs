using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TechChallenge.Oficina.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtrairOrcamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orcamentos_servicos");

            migrationBuilder.DropTable(
                name: "orcamentos");

            migrationBuilder.AddColumn<bool>(
                name: "estoque_debitado",
                table: "ordens_servico_referencias",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "estoque_debitado",
                table: "ordens_servico_referencias");

            migrationBuilder.CreateTable(
                name: "orcamentos",
                columns: table => new
                {
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_geracao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orcamentos", x => x.ordem_servico_id);
                });

            migrationBuilder.CreateTable(
                name: "orcamentos_servicos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nome_servico = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orcamentos_servicos", x => x.id);
                    table.ForeignKey(
                        name: "FK_orcamentos_servicos_orcamentos_ordem_servico_id",
                        column: x => x.ordem_servico_id,
                        principalTable: "orcamentos",
                        principalColumn: "ordem_servico_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orcamentos_servicos_ordem_servico_id",
                table: "orcamentos_servicos",
                column: "ordem_servico_id");
        }
    }
}
