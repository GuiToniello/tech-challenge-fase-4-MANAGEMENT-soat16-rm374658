using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using TechChallenge.Oficina.Adapters.Features.Orcamentos;
using TechChallenge.Oficina.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Adapters.Tests.Features.Orcamentos;

public sealed class OrcamentosAdapterTests
{
    private readonly OrcamentosAdapter _adapter = new();

    [Fact]
    public void Adapt_DeveRetornarOk_QuandoOrcamentoViewModelSucesso()
    {
        var orcamento = new OrcamentoViewModel { OrdemServicoId = Guid.NewGuid(), ValorTotal = 50m };
        var result = new OrcamentosResult<OrcamentoViewModel, Exception>(orcamento);

        var adaptado = _adapter.Adapt(result);

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(adaptado);
        Assert.Equal(orcamento, ok.Value);
    }

    [Fact]
    public void Adapt_DeveRetornarBadRequestComMensagem_QuandoDomainException()
    {
        var result = new OrcamentosResult<OrcamentoViewModel, Exception>(new DomainException("erro de regra"));

        var adaptado = _adapter.Adapt(result);

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(adaptado);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal("erro de regra", badRequest.Value!["message"]);
    }

    [Fact]
    public void Adapt_DeveRetornarNotFoundComMensagem_QuandoKeyNotFoundException()
    {
        var result = new OrcamentosResult<OrcamentoViewModel, Exception>(new KeyNotFoundException("Orcamento nao encontrado."));

        var adaptado = _adapter.Adapt(result);

        var notFound = Assert.IsType<NotFound<Dictionary<string, string?>>>(adaptado);
        Assert.Equal("Orcamento nao encontrado.", notFound.Value!["message"]);
    }

    [Fact]
    public void Adapt_DeveRetornarProblem_QuandoExcecaoInesperada()
    {
        var result = new OrcamentosResult<OrcamentoViewModel, Exception>(new InvalidOperationException("falha"));

        var adaptado = _adapter.Adapt(result);

        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(adaptado);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode.StatusCode);
    }

    [Fact]
    public void Adapt_DeveRetornarOk_QuandoBoolSucesso()
    {
        var adaptado = _adapter.Adapt(new OrcamentosResult<bool, Exception>(true));

        var ok = Assert.IsType<Ok<bool>>(adaptado);
        Assert.True(ok.Value);
    }

    [Fact]
    public void Adapt_DeveRetornarNotFound_QuandoBoolComKeyNotFoundException()
    {
        var adaptado = _adapter.Adapt(new OrcamentosResult<bool, Exception>(new KeyNotFoundException("nao encontrado")));

        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(adaptado);
        Assert.Equal(StatusCodes.Status404NotFound, statusCode.StatusCode);
    }

    [Fact]
    public void Empty_DeveRetornarNoContent()
    {
        Assert.IsType<NoContent>(_adapter.Empty());
    }
}
