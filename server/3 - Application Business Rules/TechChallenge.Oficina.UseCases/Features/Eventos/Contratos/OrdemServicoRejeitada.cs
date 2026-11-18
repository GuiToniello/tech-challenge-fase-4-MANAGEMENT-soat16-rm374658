namespace TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;

// Publicado pelo Management para o OS Service e o Billing quando os dados da OS (cliente, veiculo ou servicos) sao invalidos.
public sealed record OrdemServicoRejeitada(Guid OrdemServicoId, string Motivo);
