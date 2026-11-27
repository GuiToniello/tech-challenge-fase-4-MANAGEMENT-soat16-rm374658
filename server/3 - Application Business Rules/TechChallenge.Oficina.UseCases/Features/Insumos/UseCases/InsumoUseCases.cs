using AutoMapper;
using TechChallenge.Oficina.UseCases.Features.Insumos.Commands;
using TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.Queries;
using TechChallenge.Oficina.UseCases.Features.Insumos.ViewModels;
using TechChallenge.Oficina.Entities.Features.Insumos;

namespace TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;

// O cadastro e o ajuste do saldo vao para o historico de movimentacoes de estoque (DynamoDB) depois da gravacao no PostgreSQL:
// best-effort (o gateway nao lanca) e sem o token da requisicao, para o registro nao ser interrompido com o saldo ja gravado.
public sealed class InsumoUseCases : IInsumoUseCases
{
    private readonly IMapper _mapper;
    private readonly IInsumoGateway _insumoGateway;
    private readonly IHistoricoEstoqueGateway _historicoEstoqueGateway;

    public InsumoUseCases(IMapper mapper, IInsumoGateway insumoGateway, IHistoricoEstoqueGateway historicoEstoqueGateway)
    {
        _mapper = mapper;
        _insumoGateway = insumoGateway;
        _historicoEstoqueGateway = historicoEstoqueGateway;
    }

    public async Task<InsumoViewModel> CriarAsync(CriarInsumoCommand command, CancellationToken cancellationToken = default)
    {
        var insumo = Insumo.Criar(command.Nome, command.Fabricante, command.QuantidadeDisponivel, command.ValorUnitario);

        await _insumoGateway.AdicionarAsync(insumo, cancellationToken);
        await RegistrarMovimentacaoAsync(insumo.Id, TiposMovimentacaoEstoque.Cadastro, saldoAnterior: 0, insumo.QuantidadeDisponivel);

        return _mapper.Map<InsumoViewModel>(insumo);
    }

    public async Task<InsumoViewModel> AtualizarAsync(AtualizarInsumoCommand command, CancellationToken cancellationToken = default)
    {
        var insumo = await ObterInsumoExistenteAsync(command.Id, cancellationToken);
        var saldoAnterior = insumo.QuantidadeDisponivel;

        insumo.AtualizarNome(command.Nome);
        insumo.AtualizarFabricante(command.Fabricante);
        insumo.AtualizarQuantidadeDisponivel(command.QuantidadeDisponivel);
        insumo.AtualizarValorUnitario(command.ValorUnitario);

        await _insumoGateway.AtualizarAsync(insumo, cancellationToken);

        // So a mudanca do saldo e movimentacao de estoque: alterar nome, fabricante ou valor nao gera registro
        if (insumo.QuantidadeDisponivel != saldoAnterior)
        {
            await RegistrarMovimentacaoAsync(insumo.Id, TiposMovimentacaoEstoque.Ajuste, saldoAnterior, insumo.QuantidadeDisponivel);
        }

        return _mapper.Map<InsumoViewModel>(insumo);
    }

    public async Task<InsumoViewModel> ObterPorIdAsync(ObterInsumoPorIdQuery query, CancellationToken cancellationToken = default)
    {
        var insumo = await ObterInsumoExistenteAsync(query.Id, cancellationToken);
        return _mapper.Map<InsumoViewModel>(insumo);
    }

    public async Task<IReadOnlyCollection<InsumoViewModel>> ListarAsync(ListarInsumosQuery query, CancellationToken cancellationToken = default)
    {
        var insumos = await _insumoGateway.ListarAsync(cancellationToken);
        return _mapper.Map<IReadOnlyCollection<InsumoViewModel>>(insumos);
    }

    public async Task ExcluirAsync(ExcluirInsumoCommand command, CancellationToken cancellationToken = default)
    {
        var insumo = await ObterInsumoExistenteAsync(command.Id, cancellationToken);
        await _insumoGateway.RemoverAsync(insumo, cancellationToken);
    }

    public async Task<IReadOnlyCollection<MovimentacaoEstoqueViewModel>> ListarMovimentacoesAsync(ListarMovimentacoesEstoqueQuery query, CancellationToken cancellationToken = default)
    {
        // So o DynamoDB, sem consultar o PostgreSQL: insumo excluido ou desconhecido retorna o que houver no historico (ou lista vazia)
        var movimentacoes = await _historicoEstoqueGateway.ListarAsync(query.Id, cancellationToken);
        return _mapper.Map<IReadOnlyCollection<MovimentacaoEstoqueViewModel>>(movimentacoes.OrderBy(movimentacao => movimentacao.OcorridoEm).ToArray());
    }

    // Movimentacao feita pela API (origem acao): a quantidade e a variacao do saldo, com sinal
    private Task RegistrarMovimentacaoAsync(Guid insumoId, string tipo, int saldoAnterior, int saldoNovo)
    {
        return _historicoEstoqueGateway.RegistrarAsync(
            new MovimentacaoEstoque
            {
                InsumoId = insumoId,
                OcorridoEm = DateTime.UtcNow,
                Tipo = tipo,
                Origem = OrigensMovimentacaoEstoque.Acao,
                Quantidade = saldoNovo - saldoAnterior,
                SaldoAnterior = saldoAnterior,
                SaldoNovo = saldoNovo
            },
            CancellationToken.None);
    }

    private async Task<Insumo> ObterInsumoExistenteAsync(Guid id, CancellationToken cancellationToken)
    {
        var insumo = await _insumoGateway.ObterPorIdAsync(id, cancellationToken);

        if (insumo is null)
        {
            throw new KeyNotFoundException("Insumo não encontrado.");
        }

        return insumo;
    }
}
