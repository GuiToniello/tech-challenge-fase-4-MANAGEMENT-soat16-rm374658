using Moq;
using Resend;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Email.Configuration;
using TechChallenge.Oficina.Email.Features.OrdensServico;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;
using Xunit;
using TechChallenge.Oficina.Email.Features;

namespace TechChallenge.Oficina.Email.Tests.Features.OrdensServico;

public sealed class OrdemServicoStatusEmailSenderTests
{
    private readonly Mock<IResendClient> _resendClientMock = new();

    [Theory]
    [InlineData(StatusOrdemServico.EmDiagnostico)]
    [InlineData(StatusOrdemServico.EmExecucao)]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    public async Task EnviarStatusAlteradoAsync_DeveEnviarEmailComAssuntoCustomizadoParaStatus(StatusOrdemServico status)
    {
        var notificacao = CriarNotificacao(status);
        var sender = CriarSender();
        var emailCapturado = CapturarEmail();

        await sender.EnviarStatusAlteradoAsync(notificacao, "cliente@teste.com");

        _resendClientMock.Verify(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        var email = emailCapturado();
        Assert.Contains("onboarding@resend.dev", email.From.ToString());
        Assert.Contains("cliente@teste.com", email.To);
        Assert.Contains(notificacao.OrdemServicoId.ToString()[..8].ToUpperInvariant(), email.Subject);
        Assert.Contains("Status", email.Subject);
        Assert.Contains(notificacao.OrdemServicoId.ToString(), email.HtmlBody);
    }

    [Fact]
    public async Task EnviarStatusAlteradoAsync_DeveRetornarSemErroQuandoApiKeyNaoConfigurada()
    {
        var sender = CriarSender(new ResendSettings { ApiKey = string.Empty, FromEmail = "onboarding@resend.dev", SendEmailOnStatusChange = true });

        // Não deve lançar exceção em modo degradado
        await sender.EnviarStatusAlteradoAsync(CriarNotificacao(StatusOrdemServico.EmDiagnostico), "cliente@teste.com");

        _resendClientMock.Verify(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarStatusAlteradoAsync_DeveRetornarSemErroQuandoFromEmailNaoConfigurado()
    {
        var sender = CriarSender(new ResendSettings { ApiKey = "api-key", FromEmail = string.Empty, SendEmailOnStatusChange = true });

        // Não deve lançar exceção em modo degradado
        await sender.EnviarStatusAlteradoAsync(CriarNotificacao(StatusOrdemServico.EmExecucao), "cliente@teste.com");

        _resendClientMock.Verify(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarStatusAlteradoAsync_DeveRetornarSemErroQuandoSendEmailOnStatusChangeEstaDesabilitado()
    {
        var sender = CriarSender(new ResendSettings { ApiKey = "api-key", FromEmail = "onboarding@resend.dev", SendEmailOnStatusChange = false });

        // Não deve lançar exceção mesmo com flag desabilitada
        await sender.EnviarStatusAlteradoAsync(CriarNotificacao(StatusOrdemServico.EmDiagnostico), "cliente@teste.com");

        _resendClientMock.Verify(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarStatusAlteradoAsync_DeveConterDadosOrdemServicoNoHTML()
    {
        var notificacao = CriarNotificacao(StatusOrdemServico.Finalizada);
        var sender = CriarSender();
        var emailCapturado = CapturarEmail();

        await sender.EnviarStatusAlteradoAsync(notificacao, "cliente@teste.com");

        var email = emailCapturado();
        Assert.Contains(notificacao.OrdemServicoId.ToString(), email.HtmlBody);
        Assert.Contains(notificacao.ClienteId.ToString(), email.HtmlBody);
        Assert.Contains(notificacao.VeiculoId.ToString(), email.HtmlBody);
        Assert.Contains("finalizada", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnviarStatusAlteradoAsync_DeveConterServicosEValorDoOrcamentoNoHTML()
    {
        var notificacao = CriarNotificacao(StatusOrdemServico.EmExecucao, valorOrcamento: 150m);
        var sender = CriarSender();
        var emailCapturado = CapturarEmail();

        await sender.EnviarStatusAlteradoAsync(notificacao, "cliente@teste.com");

        var email = emailCapturado();
        foreach (var servico in notificacao.Servicos)
        {
            Assert.Contains(servico, email.HtmlBody);
        }

        Assert.Contains("Informações do Orçamento", email.HtmlBody);
        Assert.Contains("150,00", email.HtmlBody);
    }

    [Fact]
    public async Task EnviarStatusAlteradoAsync_NaoDeveConterOrcamento_QuandoNaoHouverValor()
    {
        var sender = CriarSender();
        var emailCapturado = CapturarEmail();

        await sender.EnviarStatusAlteradoAsync(CriarNotificacao(StatusOrdemServico.EmDiagnostico), "cliente@teste.com");

        Assert.DoesNotContain("Informações do Orçamento", emailCapturado().HtmlBody);
    }

    [Theory]
    [InlineData(StatusOrdemServico.EmDiagnostico, "diagnóstico")]
    [InlineData(StatusOrdemServico.EmExecucao, "execução")]
    [InlineData(StatusOrdemServico.Finalizada, "finalizada")]
    [InlineData(StatusOrdemServico.Entregue, "entregue")]
    [InlineData(StatusOrdemServico.Cancelada, "cancelada")]
    public async Task EnviarStatusAlteradoAsync_DeveConterDetalhesEspecificosPorStatus(StatusOrdemServico status, string textoEsperado)
    {
        var sender = CriarSender();
        var emailCapturado = CapturarEmail();

        await sender.EnviarStatusAlteradoAsync(CriarNotificacao(status), "cliente@teste.com");

        Assert.Contains(textoEsperado, emailCapturado().HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    private OrdemServicoStatusEmailSender CriarSender(ResendSettings? settings = null)
    {
        return new OrdemServicoStatusEmailSender(
            _resendClientMock.Object,
            settings ?? new ResendSettings { ApiKey = "api-key", FromEmail = "onboarding@resend.dev", SendEmailOnStatusChange = true });
    }

    private Func<EmailMessage> CapturarEmail()
    {
        EmailMessage? emailCapturado = null;

        _resendClientMock
            .Setup(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((message, _) => emailCapturado = message)
            .Returns(Task.CompletedTask);

        return () => emailCapturado ?? throw new InvalidOperationException("Nenhum email foi enviado.");
    }

    private static NotificacaoStatusOrdemServico CriarNotificacao(StatusOrdemServico status, decimal? valorOrcamento = null)
    {
        return new NotificacaoStatusOrdemServico
        {
            OrdemServicoId = Guid.NewGuid(),
            ClienteId = Guid.NewGuid(),
            VeiculoId = Guid.NewGuid(),
            StatusNovo = status,
            ValorOrcamento = valorOrcamento,
            Servicos = ["Revisao", "Alinhamento"]
        };
    }
}
