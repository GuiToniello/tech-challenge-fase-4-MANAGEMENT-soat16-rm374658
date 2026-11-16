using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Entities.Features.Clientes;
using TechChallenge.Oficina.Entities.Features.Insumos;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.Servicos;
using TechChallenge.Oficina.Entities.Features.Veiculos;

namespace TechChallenge.Oficina.DB.Data.Context;

public sealed class OficinaDbContext : DbContext
{
    public OficinaDbContext(DbContextOptions<OficinaDbContext> options) : base(options)
    {
    }

    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Insumo> Insumos => Set<Insumo>();
    public DbSet<ItemServico> ItensServico => Set<ItemServico>();
    public DbSet<Orcamento> Orcamentos => Set<Orcamento>();
    public DbSet<OrdemServicoReferencia> OrdensServicoReferencias => Set<OrdemServicoReferencia>();
    public DbSet<Servico> Servicos => Set<Servico>();
    public DbSet<Veiculo> Veiculos => Set<Veiculo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OficinaDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
