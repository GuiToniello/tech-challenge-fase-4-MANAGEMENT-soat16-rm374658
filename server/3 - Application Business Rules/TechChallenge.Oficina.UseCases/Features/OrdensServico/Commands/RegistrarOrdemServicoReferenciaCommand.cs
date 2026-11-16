namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.Commands;

// Dados dos eventos OrdemServicoAberta e OrdemServicoAtualizada, publicados pelo OS Service.
public sealed class RegistrarOrdemServicoReferenciaCommand
{
    public Guid OrdemServicoId { get; set; }
    public Guid ClienteId { get; set; }
    public Guid VeiculoId { get; set; }
    public IReadOnlyCollection<Guid> ServicoIds { get; set; } = [];
}
