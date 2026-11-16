using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechChallenge.Oficina.Entities.Features.OrdensServico;

namespace TechChallenge.Oficina.DB.Data.Features.OrdensServico;

// Referencia local da OS (o dono e o OS Service): sem chaves estrangeiras para clientes, veiculos e servicos, que sao validados no consumo do evento.
public sealed class OrdemServicoReferenciaConfiguration : IEntityTypeConfiguration<OrdemServicoReferencia>
{
    public void Configure(EntityTypeBuilder<OrdemServicoReferencia> builder)
    {
        builder.ToTable("ordens_servico_referencias");

        builder.HasKey(referencia => referencia.Id);

        builder.Property(referencia => referencia.Id)
            .HasColumnName("ordem_servico_id")
            .ValueGeneratedNever();

        builder.Property(referencia => referencia.ClienteId)
            .HasColumnName("cliente_id")
            .IsRequired();

        builder.Property(referencia => referencia.VeiculoId)
            .HasColumnName("veiculo_id")
            .IsRequired();

        builder.Property(referencia => referencia.Status)
            .HasColumnName("status")
            .HasColumnType("integer")
            .HasConversion<int>()
            .IsRequired();

        // Colecao primitiva mapeada pelo campo privado: no PostgreSQL vira uma coluna uuid[]
        builder.Ignore(referencia => referencia.ServicoIds);
        builder.PrimitiveCollection<List<Guid>>("_servicoIds")
            .HasColumnName("servico_ids")
            .IsRequired();
    }
}
