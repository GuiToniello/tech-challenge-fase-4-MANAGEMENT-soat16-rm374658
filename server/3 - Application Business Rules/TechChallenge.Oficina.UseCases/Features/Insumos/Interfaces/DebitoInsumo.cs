namespace TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;

// Debito de um insumo para uma ordem de servico: a quantidade debitada (positiva) e o saldo antes e depois do debito
public sealed record DebitoInsumo(Guid InsumoId, int Quantidade, int SaldoAnterior, int SaldoNovo);
