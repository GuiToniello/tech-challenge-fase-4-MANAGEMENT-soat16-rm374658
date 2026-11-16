using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.UseCases.Features.Clientes.UseCases;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Commands;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.UseCases.Features.Servicos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Veiculos.UseCases;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

// Mantem a referencia local da ordem de servico a partir dos eventos do OS Service (coreografia via SQS).
public sealed class OrdemServicoReferenciaUseCases : IOrdemServicoReferenciaUseCases
{
    private static readonly StatusOrdemServico[] StatusNotificados =
    [
        StatusOrdemServico.EmDiagnostico,
        StatusOrdemServico.EmExecucao,
        StatusOrdemServico.Finalizada,
        StatusOrdemServico.Entregue
    ];

    private readonly IOrdemServicoReferenciaGateway _referenciaGateway;
    private readonly IOrcamentoGateway _orcamentoGateway;
    private readonly IClienteGateway _clienteGateway;
    private readonly IVeiculoGateway _veiculoGateway;
    private readonly IServicoGateway _servicoGateway;
    private readonly IOrdemServicoReferenciaUseCasesFacade _facade;
    private readonly ILogger<OrdemServicoReferenciaUseCases> _logger;

    public OrdemServicoReferenciaUseCases(
        IOrdemServicoReferenciaGateway referenciaGateway,
        IOrcamentoGateway orcamentoGateway,
        IClienteGateway clienteGateway,
        IVeiculoGateway veiculoGateway,
        IServicoGateway servicoGateway,
        IOrdemServicoReferenciaUseCasesFacade facade,
        ILogger<OrdemServicoReferenciaUseCases> logger)
    {
        _referenciaGateway = referenciaGateway;
        _orcamentoGateway = orcamentoGateway;
        _clienteGateway = clienteGateway;
        _veiculoGateway = veiculoGateway;
        _servicoGateway = servicoGateway;
        _facade = facade;
        _logger = logger;
    }

    public async Task<ResultadoProcessamentoEvento> RegistrarAsync(RegistrarOrdemServicoReferenciaCommand command, CancellationToken cancellationToken = default)
    {
        if (command.OrdemServicoId == Guid.Empty)
        {
            // Sem o identificador nao ha como correlacionar a rejeicao com a OS: a excecao devolve a mensagem para a fila (nova tentativa e, depois, DLQ)
            throw new DomainException("O identificador da ordem de servico e obrigatorio.");
        }

        var servicoIds = command.ServicoIds ?? [];
        var existente = await _referenciaGateway.ObterPorIdAsync(command.OrdemServicoId, cancellationToken);

        try
        {
            await ValidarDadosAsync(command.ClienteId, command.VeiculoId, servicoIds, cancellationToken);
        }
        catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
        {
            await RejeitarAsync(command.OrdemServicoId, existente, exception, cancellationToken);
            return ResultadoProcessamentoEvento.Processado;
        }

        if (existente is null)
        {
            var referencia = OrdemServicoReferencia.Criar(command.OrdemServicoId, command.ClienteId, command.VeiculoId, servicoIds);
            await _referenciaGateway.AdicionarAsync(referencia, cancellationToken);
        }
        else
        {
            existente.Atualizar(command.ClienteId, command.VeiculoId, servicoIds);
            await _referenciaGateway.AtualizarAsync(existente, cancellationToken);
        }

        return ResultadoProcessamentoEvento.Processado;
    }

    public async Task<ResultadoProcessamentoEvento> AtualizarStatusAsync(OrdemServicoStatusAlterado evento, CancellationToken cancellationToken = default)
    {
        var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);
        var statusNovo = (StatusOrdemServico)evento.StatusNovo;

        // OS desconhecida (rejeitada ou excluida) ou evento repetido: nada a atualizar
        if (referencia is null || referencia.Status == statusNovo)
        {
            return ResultadoProcessamentoEvento.Ignorado;
        }

        referencia.AtualizarStatus(statusNovo);
        await _referenciaGateway.AtualizarAsync(referencia, cancellationToken);

        if (StatusNotificados.Contains(statusNovo))
        {
            await EnviarNotificacaoStatusAlteradoAsync(referencia, cancellationToken);
        }

        return ResultadoProcessamentoEvento.Processado;
    }

    public async Task<ResultadoProcessamentoEvento> RemoverAsync(OrdemServicoExcluida evento, CancellationToken cancellationToken = default)
    {
        var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);
        var orcamento = await _orcamentoGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        if (referencia is null && orcamento is null)
        {
            return ResultadoProcessamentoEvento.Ignorado;
        }

        if (orcamento is not null)
        {
            await _orcamentoGateway.RemoverAsync(orcamento, cancellationToken);
        }

        if (referencia is not null)
        {
            await _referenciaGateway.RemoverAsync(referencia, cancellationToken);
        }

        return ResultadoProcessamentoEvento.Processado;
    }

    private async Task ValidarDadosAsync(Guid clienteId, Guid veiculoId, IReadOnlyCollection<Guid> servicoIds, CancellationToken cancellationToken)
    {
        var cliente = await _clienteGateway.ObterPorIdAsync(clienteId, cancellationToken);

        if (cliente is null)
        {
            throw new KeyNotFoundException($"Cliente com ID '{clienteId}' nao encontrado.");
        }

        var veiculo = await _veiculoGateway.ObterPorIdAsync(veiculoId, cancellationToken);

        if (veiculo is null)
        {
            throw new KeyNotFoundException($"Veiculo com ID '{veiculoId}' nao encontrado.");
        }

        if (veiculo.ClienteId != clienteId)
        {
            throw new DomainException("O veiculo informado deve estar vinculado ao cliente da ordem de servico.");
        }

        if (servicoIds.Count == 0)
        {
            throw new DomainException("A ordem de servico deve possuir ao menos um servico.");
        }

        if (servicoIds.Any(servicoId => servicoId == Guid.Empty))
        {
            throw new DomainException("Todos os servicos informados devem possuir identificador valido.");
        }

        foreach (var servicoId in servicoIds.Distinct())
        {
            var servico = await _servicoGateway.ObterPorIdAsync(servicoId, cancellationToken);

            if (servico is null)
            {
                throw new KeyNotFoundException($"Servico com ID '{servicoId}' nao encontrado.");
            }
        }
    }

    private async Task RejeitarAsync(Guid ordemServicoId, OrdemServicoReferencia? existente, Exception motivo, CancellationToken cancellationToken)
    {
        if (existente is not null)
        {
            await _referenciaGateway.RemoverAsync(existente, cancellationToken);
        }

        await _facade.EventosPublisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, motivo.Message), ordemServicoId, cancellationToken);

        var tipoFalha = motivo is KeyNotFoundException
            ? OrdemServicoLogEventos.TiposFalha.NaoEncontrado
            : OrdemServicoLogEventos.TiposFalha.RegraNegocio;

        // Rejeicao de regra de negocio nao e erro de aplicacao: registra so o motivo, sem stack trace
        _logger.LogWarning(
            OrdemServicoLogEventos.OsFalha,
            "Falha na etapa {Etapa} da ordem de servico {OrdemServicoId} ({TipoFalha}): {Motivo}",
            existente is null ? OrdemServicoLogEventos.Etapas.Abertura : OrdemServicoLogEventos.Etapas.Atualizacao,
            ordemServicoId,
            tipoFalha,
            motivo.Message);
    }

    private async Task EnviarNotificacaoStatusAlteradoAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken)
    {
        // Modo degradado: falhas na busca dos dados ou no envio de email (inclusive timeout) não interrompem a atualização de status; só o cancelamento é propagado
        string? emailDestino;
        NotificacaoStatusOrdemServico notificacao;

        try
        {
            var cliente = await _clienteGateway.ObterPorIdAsync(referencia.ClienteId, cancellationToken);
            emailDestino = cliente?.Email;

            if (string.IsNullOrWhiteSpace(emailDestino))
            {
                return;
            }

            notificacao = await CriarNotificacaoAsync(referencia, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Falha ao buscar os dados da ordem de servico {OrdemServicoId} para notificar a alteracao de status.",
                referencia.Id);
            return;
        }

        try
        {
            await _facade.OrdemServicoStatusEmailSender.EnviarStatusAlteradoAsync(notificacao, emailDestino, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                OrdemServicoLogEventos.IntegracaoFalha,
                ex,
                "Falha na integracao {Integracao} durante a operacao {Operacao} da ordem de servico {OrdemServicoId} com status {StatusNovo}.",
                OrdemServicoLogEventos.Integracoes.Resend,
                OrdemServicoLogEventos.Operacoes.NotificacaoStatus,
                referencia.Id,
                OrdemServicoLogEventos.StatusParaLog(referencia.Status));
        }
    }

    private async Task<NotificacaoStatusOrdemServico> CriarNotificacaoAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken)
    {
        var orcamento = await _orcamentoGateway.ObterPorIdAsync(referencia.Id, cancellationToken);
        var servicos = new List<string>(referencia.ServicoIds.Count);

        foreach (var servicoId in referencia.ServicoIds)
        {
            var servico = await _servicoGateway.ObterPorIdAsync(servicoId, cancellationToken);

            if (servico is not null)
            {
                servicos.Add(servico.Nome);
            }
        }

        return new NotificacaoStatusOrdemServico
        {
            OrdemServicoId = referencia.Id,
            ClienteId = referencia.ClienteId,
            VeiculoId = referencia.VeiculoId,
            StatusNovo = referencia.Status,
            ValorOrcamento = orcamento?.ValorTotal,
            Servicos = servicos
        };
    }
}
