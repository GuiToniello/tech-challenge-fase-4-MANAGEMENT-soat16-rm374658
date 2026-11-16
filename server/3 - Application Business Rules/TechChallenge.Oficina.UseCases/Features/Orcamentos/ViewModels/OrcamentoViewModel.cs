namespace TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;

public sealed class OrcamentoViewModel
{
    public Guid OrdemServicoId { get; set; }
    public DateTime DataGeracao { get; set; }
    public decimal ValorTotal { get; set; }
    public int Status { get; set; }
    public string StatusDescricao { get; set; } = string.Empty;
    public IReadOnlyCollection<OrcamentoServicoViewModel> Servicos { get; set; } = [];
}
