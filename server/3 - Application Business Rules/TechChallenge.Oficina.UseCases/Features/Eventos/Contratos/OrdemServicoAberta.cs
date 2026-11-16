namespace TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;

// Publicado pelo OS Service e consumido pelo Management.
public sealed record OrdemServicoAberta(Guid OrdemServicoId, Guid ClienteId, Guid VeiculoId, IReadOnlyCollection<Guid> ServicoIds);
