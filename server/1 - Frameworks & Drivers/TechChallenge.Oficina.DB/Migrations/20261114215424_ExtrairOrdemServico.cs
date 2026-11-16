using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TechChallenge.Oficina.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtrairOrdemServico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indicadores");

            migrationBuilder.DropTable(
                name: "ordens_servico_historico_status");

            migrationBuilder.DropTable(
                name: "ordens_servico_orcamentos_servicos");

            migrationBuilder.DropTable(
                name: "ordens_servico_servicos");

            migrationBuilder.DropTable(
                name: "ordens_servico_orcamentos");

            migrationBuilder.DropTable(
                name: "ordens_servico");

            migrationBuilder.CreateTable(
                name: "orcamentos",
                columns: table => new
                {
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_geracao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orcamentos", x => x.ordem_servico_id);
                });

            migrationBuilder.CreateTable(
                name: "ordens_servico_referencias",
                columns: table => new
                {
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    veiculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    servico_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordens_servico_referencias", x => x.ordem_servico_id);
                });

            migrationBuilder.CreateTable(
                name: "orcamentos_servicos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_servico = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orcamentos_servicos");

            migrationBuilder.DropTable(
                name: "ordens_servico_referencias");

            migrationBuilder.DropTable(
                name: "orcamentos");

            migrationBuilder.CreateTable(
                name: "indicadores",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    tempo_medio_entrega = table.Column<TimeSpan>(type: "interval", nullable: false),
                    tempo_medio_execucao = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indicadores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ordens_servico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    veiculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordens_servico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ordens_servico_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ordens_servico_veiculos_veiculo_id",
                        column: x => x.veiculo_id,
                        principalTable: "veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ordens_servico_historico_status",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    data_alteracao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordens_servico_historico_status", x => x.id);
                    table.ForeignKey(
                        name: "FK_ordens_servico_historico_status_ordens_servico_ordem_servic~",
                        column: x => x.ordem_servico_id,
                        principalTable: "ordens_servico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ordens_servico_orcamentos",
                columns: table => new
                {
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_geracao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordens_servico_orcamentos", x => x.ordem_servico_id);
                    table.ForeignKey(
                        name: "FK_ordens_servico_orcamentos_ordens_servico_ordem_servico_id",
                        column: x => x.ordem_servico_id,
                        principalTable: "ordens_servico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ordens_servico_servicos",
                columns: table => new
                {
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    servico_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordens_servico_servicos", x => new { x.ordem_servico_id, x.servico_id });
                    table.ForeignKey(
                        name: "FK_ordens_servico_servicos_ordens_servico_ordem_servico_id",
                        column: x => x.ordem_servico_id,
                        principalTable: "ordens_servico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ordens_servico_servicos_servicos_servico_id",
                        column: x => x.servico_id,
                        principalTable: "servicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ordens_servico_orcamentos_servicos",
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
                    table.PrimaryKey("PK_ordens_servico_orcamentos_servicos", x => x.id);
                    table.ForeignKey(
                        name: "FK_ordens_servico_orcamentos_servicos_ordens_servico_orcamento~",
                        column: x => x.ordem_servico_id,
                        principalTable: "ordens_servico_orcamentos",
                        principalColumn: "ordem_servico_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_cliente_id",
                table: "ordens_servico",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_status",
                table: "ordens_servico",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_veiculo_id",
                table: "ordens_servico",
                column: "veiculo_id");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_historico_status_data_alteracao",
                table: "ordens_servico_historico_status",
                column: "data_alteracao");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_historico_status_ordem_servico_id",
                table: "ordens_servico_historico_status",
                column: "ordem_servico_id");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_orcamentos_servicos_ordem_servico_id",
                table: "ordens_servico_orcamentos_servicos",
                column: "ordem_servico_id");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_orcamentos_servicos_servico_id",
                table: "ordens_servico_orcamentos_servicos",
                column: "servico_id");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_servicos_servico_id",
                table: "ordens_servico_servicos",
                column: "servico_id");
        }
    }
}
