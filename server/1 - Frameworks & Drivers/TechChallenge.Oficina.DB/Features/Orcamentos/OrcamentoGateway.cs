using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.DB.Data.Context;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;

namespace TechChallenge.Oficina.DB.Data.Features.Orcamentos;

public sealed class OrcamentoGateway : IOrcamentoGateway
{
    private readonly OficinaDbContext _context;

    public OrcamentoGateway(OficinaDbContext context)
    {
        _context = context;
    }

    public async Task AdicionarAsync(Orcamento orcamento, CancellationToken cancellationToken = default)
    {
        await _context.Orcamentos.AddAsync(orcamento, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AtualizarAsync(Orcamento orcamento, CancellationToken cancellationToken = default)
    {
        _context.Orcamentos.Update(orcamento);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Orcamento?> ObterPorIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        // Os servicos do orcamento sao entidades owned: o EF Core ja os carrega junto com o orcamento
        return await _context.Orcamentos
            .FirstOrDefaultAsync(orcamento => orcamento.Id == ordemServicoId, cancellationToken);
    }

    public async Task RemoverAsync(Orcamento orcamento, CancellationToken cancellationToken = default)
    {
        _context.Orcamentos.Remove(orcamento);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
