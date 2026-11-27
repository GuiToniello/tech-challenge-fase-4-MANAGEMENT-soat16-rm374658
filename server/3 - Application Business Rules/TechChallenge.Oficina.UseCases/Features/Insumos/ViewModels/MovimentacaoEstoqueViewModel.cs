namespace TechChallenge.Oficina.UseCases.Features.Insumos.ViewModels;

public sealed class MovimentacaoEstoqueViewModel
{
    public DateTime OcorridoEm { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Origem { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public int SaldoAnterior { get; set; }
    public int SaldoNovo { get; set; }
    public Guid? OrdemServicoId { get; set; }
}
