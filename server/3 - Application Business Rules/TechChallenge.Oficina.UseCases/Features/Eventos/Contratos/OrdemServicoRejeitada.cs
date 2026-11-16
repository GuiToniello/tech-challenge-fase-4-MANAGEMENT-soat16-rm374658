namespace TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;

// Publicado pelo Management e consumido pelo OS Service quando os dados da OS (cliente, veiculo ou servicos) sao invalidos.
public sealed record OrdemServicoRejeitada(Guid OrdemServicoId, string Motivo);
