using TechChallenge.Oficina.Entities.Features.Insumos;
using TechChallenge.Oficina.Entities.Features.Servicos;

namespace TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;

public sealed class EstoqueUseCases : IEstoqueUseCases
{
    private readonly IInsumoGateway _insumoGateway;

    public EstoqueUseCases(IInsumoGateway insumoGateway)
    {
        _insumoGateway = insumoGateway;
    }

    // Consulta sem excecao usada na validacao da OS: a OS segue valida mesmo sem estoque, e o resultado vai no evento OrdemServicoValidada
    public async Task<bool> PossuiDisponibilidadeAsync(IReadOnlyCollection<Servico> servicos, CancellationToken cancellationToken = default)
    {
        var insumosNecessarios = AgruparInsumosPorId(servicos);

        foreach (var (insumoId, quantidadeTotal) in insumosNecessarios)
        {
            var insumo = await _insumoGateway.ObterPorIdAsync(insumoId, cancellationToken);

            if (insumo is null || insumo.QuantidadeDisponivel < quantidadeTotal)
            {
                return false;
            }
        }

        return true;
    }

    public async Task DebitarEstoqueParaOrdemServicoAsync(IReadOnlyCollection<Servico> servicos, CancellationToken cancellationToken = default)
    {
        var insumosNecessarios = AgruparInsumosPorId(servicos);
        var debitos = new List<(Insumo Insumo, int Quantidade)>(insumosNecessarios.Count);

        // Confere todos os insumos antes de gravar o primeiro debito: com estoque insuficiente nenhum insumo e debitado (sem debito parcial)
        foreach (var (insumoId, quantidadeTotal) in insumosNecessarios)
        {
            var insumo = await _insumoGateway.ObterPorIdAsync(insumoId, cancellationToken);

            if (insumo is null)
            {
                throw new KeyNotFoundException($"Insumo com ID '{insumoId}' nao encontrado.");
            }

            insumo.VerificarDisponibilidade(quantidadeTotal);
            debitos.Add((insumo, quantidadeTotal));
        }

        foreach (var (insumo, quantidade) in debitos)
        {
            insumo.DebitarEstoque(quantidade);
            await _insumoGateway.AtualizarAsync(insumo, cancellationToken);
        }
    }

    private static Dictionary<Guid, int> AgruparInsumosPorId(IReadOnlyCollection<Servico> servicos)
    {
        var insumosAgrupados = new Dictionary<Guid, int>();

        foreach (var servico in servicos)
        {
            foreach (var itemServico in servico.ItensServico)
            {
                if (insumosAgrupados.ContainsKey(itemServico.InsumoId))
                {
                    insumosAgrupados[itemServico.InsumoId] += itemServico.Quantidade;
                }
                else
                {
                    insumosAgrupados[itemServico.InsumoId] = itemServico.Quantidade;
                }
            }
        }

        return insumosAgrupados;
    }
}
