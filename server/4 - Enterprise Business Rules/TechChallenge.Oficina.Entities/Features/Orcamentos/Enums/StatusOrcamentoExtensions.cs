namespace TechChallenge.Oficina.Entities.Features.Orcamentos.Enums;

public static class StatusOrcamentoExtensions
{
    public static string ObterDescricao(this StatusOrcamento status)
    {
        return status switch
        {
            StatusOrcamento.Gerado => "Gerado",
            StatusOrcamento.Aprovado => "Aprovado",
            StatusOrcamento.Recusado => "Recusado",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status do orcamento invalido.")
        };
    }
}
