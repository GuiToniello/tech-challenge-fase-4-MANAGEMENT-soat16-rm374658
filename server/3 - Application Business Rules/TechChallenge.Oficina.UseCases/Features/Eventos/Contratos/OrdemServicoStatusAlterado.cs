namespace TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;

// Publicado pelo OS Service e consumido pelo Management. Os status seguem os valores de StatusOrdemServico.
public sealed record OrdemServicoStatusAlterado(Guid OrdemServicoId, Guid ClienteId, int StatusAnterior, int StatusNovo);
