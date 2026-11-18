using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.DB.Data.Context;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

namespace TechChallenge.Oficina.DB.Data.Features.OrdensServico;

public sealed class OrdemServicoReferenciaGateway : IOrdemServicoReferenciaGateway
{
    private readonly OficinaDbContext _context;

    public OrdemServicoReferenciaGateway(OficinaDbContext context)
    {
        _context = context;
    }

    public async Task AdicionarAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default)
    {
        await _context.OrdensServicoReferencias.AddAsync(referencia, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AtualizarAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default)
    {
        _context.OrdensServicoReferencias.Update(referencia);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrdemServicoReferencia?> ObterPorIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        return await _context.OrdensServicoReferencias
            .FirstOrDefaultAsync(referencia => referencia.Id == ordemServicoId, cancellationToken);
    }

    public async Task RemoverAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default)
    {
        _context.OrdensServicoReferencias.Remove(referencia);
        await _context.SaveChangesAsync(cancellationToken);
    }

    // Os gateways do escopo compartilham este DbContext: os SaveChanges feitos dentro da operacao entram na mesma transacao.
    // Sem commit (excecao), nada fica gravado. O provedor InMemory (testes) nao tem transacao: a operacao roda direto.
    public async Task ExecutarEmTransacaoAsync(Func<Task> operacao, CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsRelational())
        {
            await operacao();
            return;
        }

        await using var transacao = await _context.Database.BeginTransactionAsync(cancellationToken);
        await operacao();
        await transacao.CommitAsync(cancellationToken);
    }
}
