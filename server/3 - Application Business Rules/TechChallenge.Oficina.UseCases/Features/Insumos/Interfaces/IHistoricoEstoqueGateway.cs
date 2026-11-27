namespace TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;

// Porta de saida para o historico de movimentacoes de estoque (Amazon DynamoDB no Frameworks & Drivers): cada movimentacao do saldo de um insumo.
// O historico e complementar e descartavel: a fonte da verdade continua no PostgreSQL e nenhuma regra le o historico para decidir algo.
public interface IHistoricoEstoqueGateway
{
    // Best-effort: falha de integracao nao lanca excecao (a implementacao registra integracao.falha e segue)
    Task RegistrarAsync(MovimentacaoEstoque movimentacao, CancellationToken cancellationToken);

    // Movimentacoes do insumo em ordem cronologica; vazio quando nao ha registros
    Task<IReadOnlyCollection<MovimentacaoEstoque>> ListarAsync(Guid insumoId, CancellationToken cancellationToken);
}
