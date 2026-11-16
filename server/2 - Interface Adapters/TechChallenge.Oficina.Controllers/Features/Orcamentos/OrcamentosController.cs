using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Commands;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Queries;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;

namespace TechChallenge.Oficina.Controllers.Features.Orcamentos
{
    public class OrcamentosController : IOrcamentosController
    {
        private readonly IOrcamentoUseCases _orcamentoUseCases;
        private readonly IOrcamentosAdapter _orcamentosAdapter;
        private readonly ILogger<OrcamentosController> _logger;

        public OrcamentosController(
            IOrcamentoUseCases orcamentoUseCases,
            IOrcamentosAdapter orcamentosAdapter,
            ILogger<OrcamentosController> logger)
        {
            _orcamentoUseCases = orcamentoUseCases;
            _orcamentosAdapter = orcamentosAdapter;
            _logger = logger;
        }

        public async Task<object> GetById(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var query = new ObterOrcamentoPorOrdemServicoIdQuery { OrdemServicoId = ordemServicoId };
                var orcamento = await _orcamentoUseCases.ObterPorOrdemServicoIdAsync(query, cancellationToken);
                var result = OrcamentosResult.From(orcamento);

                return _orcamentosAdapter.Adapt(result);
            }
            catch (KeyNotFoundException exception)
            {
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
        }

        public async Task<object> Gerar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var orcamento = await _orcamentoUseCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = OrcamentosResult.From(orcamento);

                return _orcamentosAdapter.Adapt(result);
            }
            catch (DomainException exception)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoGeracao, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
            catch (KeyNotFoundException exception)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoGeracao, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
        }

        public async Task<object> Enviar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                await _orcamentoUseCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);

                return _orcamentosAdapter.Empty();
            }
            catch (DomainException exception)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoEnvio, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<bool>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
            catch (KeyNotFoundException exception)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoEnvio, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<bool>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
        }

        public async Task<object> Aprovar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var orcamento = await _orcamentoUseCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = OrcamentosResult.From(orcamento);

                return _orcamentosAdapter.Adapt(result);
            }
            catch (DomainException exception)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoAprovacao, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
            catch (KeyNotFoundException exception)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoAprovacao, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
        }

        public async Task<object> Recusar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var orcamento = await _orcamentoUseCases.RecusarAsync(new RecusarOrcamentoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = OrcamentosResult.From(orcamento);

                return _orcamentosAdapter.Adapt(result);
            }
            catch (DomainException exception)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoRecusa, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
            catch (KeyNotFoundException exception)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoRecusa, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
        }

        private void RegistrarFalha(string etapa, Guid ordemServicoId, Exception exception)
        {
            var tipoFalha = exception is KeyNotFoundException
                ? OrdemServicoLogEventos.TiposFalha.NaoEncontrado
                : OrdemServicoLogEventos.TiposFalha.RegraNegocio;

            // Rejeicao de regra de negocio nao e erro de aplicacao: registra so o motivo, sem stack trace
            _logger.LogWarning(
                OrdemServicoLogEventos.OsFalha,
                "Falha na etapa {Etapa} da ordem de servico {OrdemServicoId} ({TipoFalha}): {Motivo}",
                etapa,
                ordemServicoId,
                tipoFalha,
                exception.Message);
        }
    }
}
