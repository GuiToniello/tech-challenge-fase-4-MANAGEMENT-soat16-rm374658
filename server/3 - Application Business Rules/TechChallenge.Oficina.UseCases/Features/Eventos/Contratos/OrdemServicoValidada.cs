namespace TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;

// Publicado pelo Management para o OS Service e o Billing quando os dados da OS (cliente, veiculo e servicos) sao validos: leva os servicos precificados para o orcamento.
public sealed record OrdemServicoValidada(
    Guid OrdemServicoId,
    Guid ClienteId,
    Guid VeiculoId,
    string? ClienteEmail,
    bool EstoqueSuficiente,
    IReadOnlyCollection<ServicoValidado> Servicos,
    decimal ValorTotal);
