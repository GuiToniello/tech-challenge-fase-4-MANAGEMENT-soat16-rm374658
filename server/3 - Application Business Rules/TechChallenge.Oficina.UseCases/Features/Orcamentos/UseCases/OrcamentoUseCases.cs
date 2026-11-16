using AutoMapper;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.Servicos;
using TechChallenge.Oficina.UseCases.Features.Clientes.UseCases;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Commands;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Queries;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.UseCases.Features.Servicos.UseCases;

namespace TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;

// O orcamento e do Management; a OS e do OS Service, que e informado das decisoes por eventos (OrcamentoGerado/Aprovado/Recusado).
public sealed class OrcamentoUseCases : IOrcamentoUseCases
{
    private readonly IMapper _mapper;
    private readonly IOrcamentoGateway _orcamentoGateway;
    private readonly IOrdemServicoReferenciaGateway _referenciaGateway;
    private readonly IClienteGateway _clienteGateway;
    private readonly IServicoGateway _servicoGateway;
    private readonly IOrcamentoUseCasesFacade _facade;
    private readonly ILogger<OrcamentoUseCases> _logger;

    public OrcamentoUseCases(
        IMapper mapper,
        IOrcamentoGateway orcamentoGateway,
        IOrdemServicoReferenciaGateway referenciaGateway,
        IClienteGateway clienteGateway,
        IServicoGateway servicoGateway,
        IOrcamentoUseCasesFacade facade,
        ILogger<OrcamentoUseCases> logger)
    {
        _mapper = mapper;
        _orcamentoGateway = orcamentoGateway;
        _referenciaGateway = referenciaGateway;
        _clienteGateway = clienteGateway;
        _servicoGateway = servicoGateway;
        _facade = facade;
        _logger = logger;
    }

    public async Task<OrcamentoViewModel> ObterPorOrdemServicoIdAsync(ObterOrcamentoPorOrdemServicoIdQuery query, CancellationToken cancellationToken = default)
    {
        var orcamento = await ObterOrcamentoExistenteAsync(query.OrdemServicoId, cancellationToken);
        return _mapper.Map<OrcamentoViewModel>(orcamento);
    }

    public async Task<OrcamentoViewModel> GerarAsync(GerarOrcamentoCommand command, CancellationToken cancellationToken = default)
    {
        var referencia = await ObterReferenciaExistenteAsync(command.OrdemServicoId, cancellationToken);
        referencia.ValidarGeracaoOrcamento();

        var orcamento = await _orcamentoGateway.ObterPorIdAsync(referencia.Id, cancellationToken);
        orcamento?.ValidarRegeracao();

        var servicos = await ObterServicosAsync(referencia.ServicoIds, cancellationToken);
        await _facade.EstoqueUseCases.VerificarDisponibilidadeParaOrcamentoAsync(servicos, cancellationToken);

        var regerado = orcamento is not null;

        if (orcamento is null)
        {
            orcamento = Orcamento.Criar(referencia.Id, servicos);
            await _orcamentoGateway.AdicionarAsync(orcamento, cancellationToken);
        }
        else
        {
            orcamento.Regerar(servicos);
            await _orcamentoGateway.AtualizarAsync(orcamento, cancellationToken);
        }

        await _facade.EventosPublisher.PublicarAsync(new OrcamentoGerado(orcamento.OrdemServicoId, orcamento.ValorTotal), orcamento.OrdemServicoId, cancellationToken);

        _logger.LogInformation(
            OrdemServicoLogEventos.OsOrcamentoGerado,
            "Orcamento da ordem de servico {OrdemServicoId} gerado com valor total {ValorTotal} (regerado: {Regerado}).",
            orcamento.OrdemServicoId,
            orcamento.ValorTotal,
            regerado);

        return _mapper.Map<OrcamentoViewModel>(orcamento);
    }

    public async Task EnviarPorEmailAsync(EnviarOrcamentoCommand command, CancellationToken cancellationToken = default)
    {
        var orcamento = await ObterOrcamentoExistenteAsync(command.OrdemServicoId, cancellationToken);
        var referencia = await ObterReferenciaExistenteAsync(orcamento.OrdemServicoId, cancellationToken);

        var cliente = await _clienteGateway.ObterPorIdAsync(referencia.ClienteId, cancellationToken)
            ?? throw new KeyNotFoundException("Cliente não encontrado.");

        if (string.IsNullOrWhiteSpace(cliente.Email))
        {
            throw new DomainException("O cliente da ordem de servico nao possui email cadastrado.");
        }

        // O timeout do HttpClient também é OperationCanceledException; só o cancelamento da requisição sai sem registro.
        try
        {
            await _facade.OrcamentoEmailSender.EnviarOrcamentoAsync(orcamento, cliente.Email, cancellationToken);
        }
        catch (Exception ex) when (ex is not DomainException && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            _logger.LogError(
                OrdemServicoLogEventos.IntegracaoFalha,
                ex,
                "Falha na integracao {Integracao} durante a operacao {Operacao} da ordem de servico {OrdemServicoId}.",
                OrdemServicoLogEventos.Integracoes.Resend,
                OrdemServicoLogEventos.Operacoes.EnvioOrcamento,
                orcamento.OrdemServicoId);
            throw;
        }

        _logger.LogInformation(
            OrdemServicoLogEventos.OsOrcamentoEnviado,
            "Orcamento da ordem de servico {OrdemServicoId} enviado por email com valor total {ValorTotal}.",
            orcamento.OrdemServicoId,
            orcamento.ValorTotal);
    }

    public async Task<OrcamentoViewModel> AprovarAsync(AprovarOrcamentoCommand command, CancellationToken cancellationToken = default)
    {
        var orcamento = await ObterOrcamentoExistenteAsync(command.OrdemServicoId, cancellationToken);
        orcamento.ValidarAprovacao();

        // A fila FIFO garante que o OS Service processa o OrcamentoGerado antes do OrcamentoAprovado; por isso nao se exige AguardandoAprovacao aqui.
        var referencia = await ObterReferenciaExistenteAsync(orcamento.OrdemServicoId, cancellationToken);
        referencia.ValidarAprovacaoOrcamento();

        var servicos = await ObterServicosAsync(referencia.ServicoIds, cancellationToken);
        await _facade.EstoqueUseCases.VerificarDisponibilidadeParaOrcamentoAsync(servicos, cancellationToken);
        await _facade.EstoqueUseCases.DebitarEstoqueParaOrdemServicoAsync(servicos, cancellationToken);

        orcamento.Aprovar();
        await _orcamentoGateway.AtualizarAsync(orcamento, cancellationToken);
        await _facade.EventosPublisher.PublicarAsync(new OrcamentoAprovado(orcamento.OrdemServicoId), orcamento.OrdemServicoId, cancellationToken);

        return _mapper.Map<OrcamentoViewModel>(orcamento);
    }

    public async Task<OrcamentoViewModel> RecusarAsync(RecusarOrcamentoCommand command, CancellationToken cancellationToken = default)
    {
        var orcamento = await ObterOrcamentoExistenteAsync(command.OrdemServicoId, cancellationToken);
        orcamento.ValidarRecusa();

        var referencia = await ObterReferenciaExistenteAsync(orcamento.OrdemServicoId, cancellationToken);
        referencia.ValidarRecusaOrcamento();

        orcamento.Recusar();
        await _orcamentoGateway.AtualizarAsync(orcamento, cancellationToken);
        await _facade.EventosPublisher.PublicarAsync(new OrcamentoRecusado(orcamento.OrdemServicoId), orcamento.OrdemServicoId, cancellationToken);

        return _mapper.Map<OrcamentoViewModel>(orcamento);
    }

    private async Task<Orcamento> ObterOrcamentoExistenteAsync(Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var orcamento = await _orcamentoGateway.ObterPorIdAsync(ordemServicoId, cancellationToken);

        if (orcamento is null)
        {
            throw new KeyNotFoundException("Orcamento nao encontrado.");
        }

        return orcamento;
    }

    private async Task<OrdemServicoReferencia> ObterReferenciaExistenteAsync(Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var referencia = await _referenciaGateway.ObterPorIdAsync(ordemServicoId, cancellationToken);

        if (referencia is null)
        {
            throw new KeyNotFoundException("Ordem de servico nao encontrada.");
        }

        return referencia;
    }

    private async Task<IReadOnlyCollection<Servico>> ObterServicosAsync(IReadOnlyCollection<Guid> servicoIds, CancellationToken cancellationToken)
    {
        var servicos = new List<Servico>(servicoIds.Count);

        foreach (var servicoId in servicoIds)
        {
            var servico = await _servicoGateway.ObterPorIdAsync(servicoId, cancellationToken);

            if (servico is null)
            {
                throw new KeyNotFoundException($"Servico com ID '{servicoId}' nao encontrado.");
            }

            servicos.Add(servico);
        }

        return servicos;
    }
}
