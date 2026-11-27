namespace TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;

// O que originou a movimentacao de estoque (atributo Origem do item no DynamoDB)
public static class OrigensMovimentacaoEstoque
{
    // Requisicao HTTP na API da Management (cadastro ou atualizacao do insumo)
    public const string Acao = "acao";

    // Evento consumido da fila (PagamentoAprovado do Billing)
    public const string Evento = "evento";
}
