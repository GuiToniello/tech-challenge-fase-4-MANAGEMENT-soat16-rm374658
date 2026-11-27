namespace TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;

// Tipo da movimentacao de estoque (atributo Tipo do item no DynamoDB)
public static class TiposMovimentacaoEstoque
{
    // Saldo inicial do insumo cadastrado
    public const string Cadastro = "Cadastro";

    // Alteracao do saldo na atualizacao do insumo
    public const string Ajuste = "Ajuste";

    // Debito dos insumos da ordem de servico quando o Billing aprova o pagamento
    public const string DebitoOrdemServico = "DebitoOrdemServico";
}
