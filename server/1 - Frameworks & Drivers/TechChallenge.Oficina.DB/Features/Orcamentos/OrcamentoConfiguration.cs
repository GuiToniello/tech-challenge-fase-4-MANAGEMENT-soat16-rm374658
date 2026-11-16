using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechChallenge.Oficina.Entities.Features.Orcamentos;

namespace TechChallenge.Oficina.DB.Data.Features.Orcamentos;

public sealed class OrcamentoConfiguration : IEntityTypeConfiguration<Orcamento>
{
    private const string OrdemServicoIdColumnName = "ordem_servico_id";

    public void Configure(EntityTypeBuilder<Orcamento> builder)
    {
        builder.ToTable("orcamentos");

        builder.HasKey(orcamento => orcamento.Id);

        builder.Property(orcamento => orcamento.Id)
            .HasColumnName(OrdemServicoIdColumnName)
            .ValueGeneratedNever();

        builder.Ignore(orcamento => orcamento.OrdemServicoId);

        builder.Property(orcamento => orcamento.DataGeracao)
            .HasColumnName("data_geracao")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(orcamento => orcamento.ValorTotal)
            .HasColumnName("valor_total")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(orcamento => orcamento.Status)
            .HasColumnName("status")
            .HasColumnType("integer")
            .HasConversion<int>()
            .IsRequired();

        builder.OwnsMany(orcamento => orcamento.Servicos, orcamentoServicoBuilder =>
        {
            orcamentoServicoBuilder.ToTable("orcamentos_servicos");
            orcamentoServicoBuilder.WithOwner().HasForeignKey(OrdemServicoIdColumnName);
            orcamentoServicoBuilder.Property<int>("id").ValueGeneratedOnAdd();
            orcamentoServicoBuilder.HasKey("id");

            orcamentoServicoBuilder.Property(orcamentoServico => orcamentoServico.ServicoId)
                .HasColumnName("servico_id")
                .IsRequired();

            orcamentoServicoBuilder.Property(orcamentoServico => orcamentoServico.NomeServico)
                .HasColumnName("nome_servico")
                .HasMaxLength(150)
                .IsRequired();

            orcamentoServicoBuilder.Property(orcamentoServico => orcamentoServico.ValorTotal)
                .HasColumnName("valor_total")
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            orcamentoServicoBuilder.HasIndex(OrdemServicoIdColumnName);
        });
    }
}
