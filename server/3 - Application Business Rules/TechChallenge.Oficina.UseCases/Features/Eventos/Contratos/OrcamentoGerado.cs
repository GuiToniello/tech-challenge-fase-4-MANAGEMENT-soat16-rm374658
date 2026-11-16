namespace TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;

// Publicado pelo Management e consumido pelo OS Service.
public sealed record OrcamentoGerado(Guid OrdemServicoId, decimal ValorTotal);
