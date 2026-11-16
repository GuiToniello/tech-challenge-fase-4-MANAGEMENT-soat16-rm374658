using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TechChallenge.Oficina.Management.API.Extensions;
using TechChallenge.Oficina.Management.API.Features.Clientes;
using TechChallenge.Oficina.Management.API.Features.Insumos;
using TechChallenge.Oficina.Management.API.Features.Orcamentos;
using TechChallenge.Oficina.Management.API.Features.Servicos;
using TechChallenge.Oficina.Management.API.Features.Veiculos;
using TechChallenge.Oficina.Adapters.Features.Clientes;
using TechChallenge.Oficina.Adapters.Features.Insumos;
using TechChallenge.Oficina.Adapters.Features.Orcamentos;
using TechChallenge.Oficina.Adapters.Features.Servicos;
using TechChallenge.Oficina.Adapters.Features.Veiculos;
using TechChallenge.Oficina.Management.API.Middleware;
using TechChallenge.Oficina.Management.API.Settings;
using TechChallenge.Oficina.Controllers.Features.Clientes;
using TechChallenge.Oficina.Controllers.Features.Eventos;
using TechChallenge.Oficina.Controllers.Features.Insumos;
using TechChallenge.Oficina.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Controllers.Features.Servicos;
using TechChallenge.Oficina.Controllers.Features.Veiculos;
using TechChallenge.Oficina.DB.Data;
using TechChallenge.Oficina.Email;
using TechChallenge.Oficina.Email.Configuration;
using TechChallenge.Oficina.Messaging;
using TechChallenge.Oficina.Messaging.Configuration;
using TechChallenge.Oficina.UseCases.Features.Clientes.Mappings;
using TechChallenge.Oficina.UseCases.Features.Clientes.UseCases;
using TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Mappings;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.UseCases.Features.Servicos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Veiculos.UseCases;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddLoggingConfiguration(builder.Configuration);
builder.Services.AddSwaggerConfiguration();
builder.Services.AddAuthConfiguration(builder.Configuration);

var databaseSettings = builder.Configuration
    .GetSection(DatabaseSettings.SectionName)
    .Get<DatabaseSettings>()
    ?? throw new InvalidOperationException("A configuração de banco de dados é obrigatória.");

if (string.IsNullOrWhiteSpace(databaseSettings.ConnectionString))
{
    throw new InvalidOperationException("A connection string do banco de dados é obrigatória.");
}

builder.Services.AddAutoMapper(_ => { }, typeof(ClienteProfile).Assembly, typeof(OrcamentoProfile).Assembly);
builder.Services.AddScoped<IClienteUseCases, ClienteUseCases>();
builder.Services.AddScoped<IInsumoUseCases, InsumoUseCases>();
builder.Services.AddScoped<IEstoqueUseCases, EstoqueUseCases>();
builder.Services.AddScoped<IOrcamentoUseCasesFacade, OrcamentoUseCasesFacade>();
builder.Services.AddScoped<IOrcamentoUseCases, OrcamentoUseCases>();
builder.Services.AddScoped<IOrdemServicoReferenciaUseCasesFacade, OrdemServicoReferenciaUseCasesFacade>();
builder.Services.AddScoped<IOrdemServicoReferenciaUseCases, OrdemServicoReferenciaUseCases>();
builder.Services.AddScoped<IServicoUseCases, ServicoUseCases>();
builder.Services.AddScoped<IVeiculoUseCases, VeiculoUseCases>();
builder.Services.AddInfraData(databaseSettings.ConnectionString);

var resendSettings = builder.Configuration
    .GetSection(ResendSettings.SectionName)
    .Get<ResendSettings>()
    ?? new ResendSettings();
builder.Services.AddInfraEmail(resendSettings);

// Integracao com o OS Service por eventos (Amazon SQS); credenciais e regiao pela cadeia padrao do AWS SDK
var sqsSettings = builder.Configuration
    .GetSection(SqsSettings.SectionName)
    .Get<SqsSettings>()
    ?? new SqsSettings();

if (string.IsNullOrWhiteSpace(sqsSettings.FilaPublicacao))
{
    throw new InvalidOperationException("A fila de publicação do SQS (SqsSettings:FilaPublicacao) é obrigatória.");
}

builder.Services.AddInfraMessaging(sqsSettings);

builder.Services.AddScoped<IClienteAdapter, ClienteAdapter>();
builder.Services.AddScoped<IClienteController, ClienteController>();
builder.Services.AddScoped<IServicoController, ServicoController>();
builder.Services.AddScoped<IServicoAdapter, ServicoAdapter>();
builder.Services.AddScoped<IOrcamentosController, OrcamentosController>();
builder.Services.AddScoped<IOrcamentosAdapter, OrcamentosAdapter>();
builder.Services.AddScoped<IVeiculoController, VeiculoController>();
builder.Services.AddScoped<IVeiculoAdapter, VeiculoAdapter>();
builder.Services.AddScoped<IInsumoController, InsumoController>();
builder.Services.AddScoped<IInsumoAdapter, InsumoAdapter>();
builder.Services.AddScoped<IEventosController, EventosController>();

var app = builder.Build();

try
{
    app.Services.ApplyMigrations();
}
catch (Exception ex)
{
    app.Logger.LogCritical(new EventId(9002, "app.migracao_falhou"), ex, "Falha ao aplicar as migrations do banco");
    Environment.ExitCode = 1;
    return;
}

app.UseLoggingConfiguration();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseSwaggerConfiguration();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapClienteEndpoints();
app.MapInsumoEndpoints();
app.MapVeiculoEndpoints();
app.MapServicoEndpoints();
app.MapOrcamentoEndpoints();

await app.RunAsync();
