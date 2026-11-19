namespace TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;

// Publicado pelo Management para o OS Service, o Billing e o Execucao e Producao quando os dados da OS (cliente, veiculo ou servicos) sao invalidos.
public sealed record OrdemServicoRejeitada(Guid OrdemServicoId, string Motivo);
