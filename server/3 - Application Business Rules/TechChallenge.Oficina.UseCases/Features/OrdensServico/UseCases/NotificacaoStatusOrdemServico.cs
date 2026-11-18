using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

// Dados da OS enviados no email de alteracao de status (a OS em si pertence ao OS Service).
public sealed class NotificacaoStatusOrdemServico
{
    public Guid OrdemServicoId { get; set; }
    public Guid ClienteId { get; set; }
    public Guid VeiculoId { get; set; }
    public StatusOrdemServico StatusNovo { get; set; }
    public IReadOnlyCollection<string> Servicos { get; set; } = [];
}
