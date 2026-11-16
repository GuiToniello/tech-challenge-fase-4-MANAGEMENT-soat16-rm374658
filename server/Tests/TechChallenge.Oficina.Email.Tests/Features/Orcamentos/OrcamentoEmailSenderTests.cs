using Moq;
using Resend;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Insumos;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.Servicos;
using TechChallenge.Oficina.Email.Configuration;
using TechChallenge.Oficina.Email.Features.Orcamentos;
using Xunit;
using TechChallenge.Oficina.Email.Features;

namespace TechChallenge.Oficina.DB.Email.Tests.Features.Orcamentos;

public sealed class OrcamentoEmailSenderTests
{
    private readonly Mock<IResendClient> _resendClientMock = new();

    [Fact]
    public async Task EnviarOrcamentoAsync_DeveMontarEmailEEnviar()
    {
        var orcamento = CriarOrcamento();
        var settings = new ResendSettings { ApiKey = "api-key", FromEmail = "onboarding@resend.dev" };
        var sender = new OrcamentoEmailSender(_resendClientMock.Object, settings);
        EmailMessage? emailCapturado = null;

        _resendClientMock
            .Setup(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((message, _) => emailCapturado = message)
            .Returns(Task.CompletedTask);

        await sender.EnviarOrcamentoAsync(orcamento, "cliente@teste.com");

        _resendClientMock.Verify(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(emailCapturado);
        Assert.Contains("onboarding@resend.dev", emailCapturado!.From.ToString());
        Assert.Contains("cliente@teste.com", emailCapturado.To);
        Assert.Contains(orcamento.OrdemServicoId.ToString(), emailCapturado.Subject);
        Assert.Contains("data:", emailCapturado.Subject);
        Assert.Contains(orcamento.OrdemServicoId.ToString(), emailCapturado.HtmlBody);
        Assert.Contains("Valor total", emailCapturado.HtmlBody);
        Assert.Contains("Status do orçamento:</strong> Gerado", emailCapturado.HtmlBody);
        Assert.Contains("Troca de oleo", emailCapturado.HtmlBody);
    }

    [Fact]
    public async Task EnviarOrcamentoAsync_DeveLancarQuandoOrcamentoNulo()
    {
        var settings = new ResendSettings { ApiKey = "api-key", FromEmail = "onboarding@resend.dev" };
        var sender = new OrcamentoEmailSender(_resendClientMock.Object, settings);

        var action = async () => await sender.EnviarOrcamentoAsync(null!, "cliente@teste.com");

        await Assert.ThrowsAsync<ArgumentNullException>(action);
        _resendClientMock.Verify(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarOrcamentoAsync_DeveLancarDomainExceptionQuandoApiKeyNaoConfigurada()
    {
        var orcamento = CriarOrcamento();
        var settings = new ResendSettings { ApiKey = string.Empty, FromEmail = "onboarding@resend.dev" };
        var sender = new OrcamentoEmailSender(_resendClientMock.Object, settings);

        var action = async () => await sender.EnviarOrcamentoAsync(orcamento, "cliente@teste.com");

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Servico de email nao esta configurado. Verifique as configuracoes do Resend.", exception.Message);
    }

    [Fact]
    public async Task EnviarOrcamentoAsync_DeveLancarDomainExceptionQuandoFromEmailNaoConfigurado()
    {
        var orcamento = CriarOrcamento();
        var settings = new ResendSettings { ApiKey = "api-key", FromEmail = string.Empty };
        var sender = new OrcamentoEmailSender(_resendClientMock.Object, settings);

        var action = async () => await sender.EnviarOrcamentoAsync(orcamento, "cliente@teste.com");

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Servico de email nao esta configurado. Verifique as configuracoes do Resend.", exception.Message);
    }

    private static Orcamento CriarOrcamento()
    {
        var insumo = Insumo.Criar("Insumo", "Fabricante", 10, 25m);
        var servico = Servico.Criar("Troca de oleo", "Descricao", [ItemServico.Criar(insumo, 2)]);
        return Orcamento.Criar(Guid.NewGuid(), [servico], new DateTime(2026, 06, 06, 10, 00, 00, DateTimeKind.Utc));
    }
}
