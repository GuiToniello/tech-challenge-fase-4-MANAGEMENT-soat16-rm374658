namespace TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;

// Publicado pelo Billing e consumido pelo Management, que debita o estoque dos insumos da OS.
public sealed record PagamentoAprovado(Guid OrdemServicoId, Guid PagamentoId, decimal Valor);
