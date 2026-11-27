namespace TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;

// Um registro do historico de movimentacoes de estoque de um insumo. Tipo usa os valores de TiposMovimentacaoEstoque e Origem os de
// OrigensMovimentacaoEstoque. Quantidade e a variacao com sinal (positiva no cadastro, positiva ou negativa no ajuste, negativa no debito)
// e os saldos sao os de antes e depois da movimentacao. OrdemServicoId so no debito por ordem de servico. Sem dados pessoais.
public sealed record MovimentacaoEstoque
{
    public required Guid InsumoId { get; init; }
    public required DateTime OcorridoEm { get; init; }
    public required string Tipo { get; init; }
    public required string Origem { get; init; }
    public required int Quantidade { get; init; }
    public required int SaldoAnterior { get; init; }
    public required int SaldoNovo { get; init; }
    public Guid? OrdemServicoId { get; init; }
}
