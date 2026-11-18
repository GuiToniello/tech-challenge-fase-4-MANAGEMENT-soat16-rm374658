# ADR-012: Controle de Estoque de Insumos

**Status**: Aceita (atualizada na fase 4: a OS mudou para o OS Service, ver [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md), e o orçamento, para o Billing, ver [ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md). O estoque continua na Management: a verificação passou para a validação da OS, e o débito, para o pagamento aprovado, pelos eventos da [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md))

**Contexto**:
O sistema de oficina gerencia ordens de serviço que consomem insumos (peças, óleos, filtros, etc.). Cada serviço possui itens que referenciam insumos com quantidade necessária. Atualmente, não há controle de disponibilidade de estoque ao gerar orçamentos ou ao aprovar e iniciar a execução de ordens de serviço, podendo resultar em orçamentos inviáveis ou execução sem recursos.

Na fase 4, o estoque e os cadastros ficam na Management, e o orçamento e o pagamento ficam no Billing, cada um com o próprio banco. A Management não recebe chamadas do Billing: a informação vai e volta por eventos.

**Decisão**:

1. **Verificação na validação da OS** (consumo de `OrdemServicoAberta` ou `OrdemServicoAtualizada`):
   - Depois de validar os cadastros, a Management confere se há estoque suficiente de todos os insumos dos serviços da OS (`EstoqueUseCases.PossuiDisponibilidadeAsync`).
   - A verificação é apenas informativa, **sem reservar** os insumos, e não lança exceção: a OS continua válida mesmo sem estoque.
   - O resultado vai no evento `OrdemServicoValidada` (`estoqueSuficiente`), junto com os serviços e os preços. O Billing não gera o orçamento com `estoqueSuficiente = false` (responde 400, "Estoque insuficiente para gerar orcamento.").

2. **Débito no pagamento aprovado** (consumo de `PagamentoAprovado`, publicado pelo Billing):
   - A Management **debita automaticamente** a quantidade de insumos do estoque (`OrdemServicoReferenciaUseCases.ProcessarPagamentoAprovadoAsync`), usando os serviços guardados na referência da OS.
   - Antes de gravar o primeiro débito, confere todos os insumos: com estoque insuficiente, nenhum insumo é debitado.
   - **Uma vez por OS:** a referência da OS guarda que o estoque já foi debitado (`EstoqueDebitado`, coluna `estoque_debitado`). Um `PagamentoAprovado` repetido, ou de uma OS que a Management não conhece, é ignorado.
   - Se faltar estoque, ou se um serviço ou insumo da OS não existir mais, sai o log `os.falha` (etapa `estoque_debito`, nível Warning) e o evento é ignorado. Não há nova tentativa nem compensação, porque o pagamento já foi aprovado no Billing: o caso fica para tratamento manual.
   - A mudança de status da OS para `EmExecucao` acontece no OS Service, que consome o mesmo `PagamentoAprovado` (ADR-020).

3. **Sem Sistema de Reservas**:
   - Não há reserva de insumos entre a validação da OS e o pagamento.
   - O controle é baseado em verificação pontual da quantidade disponível no momento da operação.

4. **Caso de Uso de Estoque**:
	- Interface `IEstoqueUseCases` no projeto **Application (UseCases)** (contrato da orquestração de estoque).
	- Implementação `EstoqueUseCases` no projeto **Application (UseCases)**, consumida pelo fluxo da referência da OS (`OrdemServicoReferenciaUseCases`) via `IOrdemServicoReferenciaUseCasesFacade`. Ela recebe os serviços da OS, carregados a partir dos ids guardados na referência da OS.
	- Justificativa: a lógica cruza múltiplos insumos e requer acesso a portas de saída (Gateways), não cabendo em uma única entidade.

5. **Métodos na Entidade `Insumo`**:
   - `VerificarDisponibilidade(int quantidadeNecessaria)`: valida se há estoque >= quantidade, lança exceção se insuficiente.
   - `DebitarEstoque(int quantidade)`: valida e decrementa `QuantidadeDisponivel`, lança exceção se insuficiente.
   - Mantém lógica de negócio no domínio (entidades ricas, ADR-002).

6. **Sem Auditoria de Movimentação**:
   - Não há necessidade de histórico de movimentações de estoque (entrada, saída, reserva).
   - Apenas atualiza a propriedade `QuantidadeDisponivel` do insumo.

**Consequências**:

- ✅ Garante que orçamentos gerados são viáveis em termos de estoque, no momento da validação da OS.
- ✅ O estoque só sai com o pagamento aprovado, e uma vez só por OS, mesmo com eventos repetidos.
- ✅ Mantém lógica de negócio no domínio (DDD).
- ✅ Simplicidade: sem entidades de movimentação ou reserva.
- ⚠ O `estoqueSuficiente` é de quando a OS foi validada: o estoque pode ser consumido por outras ordens antes da geração do orçamento ou do pagamento ("race condition" aceita pelo negócio).
- ⚠ Pagamento aprovado sem estoque não é desfeito: a falha fica registrada (`os.falha`, etapa `estoque_debito`) para tratamento manual.
- ✅ O débito dos insumos e a marca `estoque_debitado` da OS são gravados numa única transação do banco: uma falha no meio desfaz tudo, e a nova tentativa do evento debita uma vez só.
- ⚠ Sem histórico de movimentações (aceito pelo negócio).

**Fluxo de Uso** (resumo de `OrdemServicoReferenciaUseCases`, na Management):

```csharp
// 1. Validação da OS (OrdemServicoAberta/OrdemServicoAtualizada): informa o estoque, sem bloquear a OS
var servicosValidados = servicos
	.Select(servico => new ServicoValidado(servico.Id, servico.Nome, servico.CalcularValor()))
	.ToArray();
var valorTotal = decimal.Round(servicosValidados.Sum(servico => servico.Valor), 2, MidpointRounding.ToEven);
var estoqueSuficiente = await _facade.EstoqueUseCases.PossuiDisponibilidadeAsync(servicos, cancellationToken);

var evento = new OrdemServicoValidada(ordemServicoId, cliente.Id, veiculoId, cliente.Email, estoqueSuficiente, servicosValidados, valorTotal);
await _facade.EventosPublisher.PublicarAsync(evento, ordemServicoId, cancellationToken);

// 2. Pagamento aprovado (PagamentoAprovado, do Billing): debita uma vez por OS
public async Task<ResultadoProcessamentoEvento> ProcessarPagamentoAprovadoAsync(PagamentoAprovado evento, CancellationToken cancellationToken = default)
{
	var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

	// OS desconhecida (rejeitada ou excluida) ou pagamento repetido: o estoque e debitado uma unica vez por OS
	if (referencia is null || referencia.EstoqueDebitado)
		return ResultadoProcessamentoEvento.Ignorado;

	try
	{
		var servicos = await ObterServicosAsync(referencia.ServicoIds, cancellationToken);

		// Debito dos insumos e marca na referencia na mesma transacao do banco (tudo ou nada)
		await _referenciaGateway.ExecutarEmTransacaoAsync(async () =>
		{
			await _facade.EstoqueUseCases.DebitarEstoqueParaOrdemServicoAsync(servicos, cancellationToken);
			referencia.RegistrarDebitoEstoque();
			await _referenciaGateway.AtualizarAsync(referencia, cancellationToken);
		}, cancellationToken);
	}
	catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
	{
		// O pagamento ja foi aprovado no Billing: sem nova tentativa nem compensacao, a falha fica registrada para tratamento manual
		RegistrarFalha(OrdemServicoLogEventos.Etapas.EstoqueDebito, evento.OrdemServicoId, exception);
		return ResultadoProcessamentoEvento.Ignorado;
	}

	return ResultadoProcessamentoEvento.Processado;
}
```

O preço de cada serviço enviado ao Billing vem de `Servico.CalcularValor()`: a soma de quantidade × valor unitário dos insumos do serviço, arredondada em 2 casas (arredondamento bancário, `MidpointRounding.ToEven`).

**Exemplo de Validação no Domínio**:

```csharp
// Entidade Insumo
public void VerificarDisponibilidade(int quantidadeNecessaria)
{
	if (quantidadeNecessaria <= 0)
		throw new DomainException("A quantidade necessaria deve ser maior que zero.");

	if (QuantidadeDisponivel < quantidadeNecessaria)
		throw new DomainException($"Estoque insuficiente do insumo '{Nome}'. Disponivel: {QuantidadeDisponivel}, Necessario: {quantidadeNecessaria}.");
}

public void DebitarEstoque(int quantidade)
{
	if (quantidade <= 0)
		throw new DomainException("A quantidade a debitar deve ser maior que zero.");

	if (QuantidadeDisponivel < quantidade)
		throw new DomainException($"Estoque insuficiente do insumo '{Nome}'. Disponivel: {QuantidadeDisponivel}, Solicitado: {quantidade}.");

	AtualizarQuantidadeDisponivel(QuantidadeDisponivel - quantidade);
}
```

**Alternativas Consideradas**:

1. **Reservar estoque ao gerar orçamento**: Rejeitado por adicionar complexidade desnecessária e exigir mecanismo de liberação.
2. **Entidade de Movimentação de Estoque**: Rejeitado pois não há necessidade de auditoria.
3. **Value Object para Estoque**: Rejeitado pois estoque é mutável e tem persistência individual.
4. **Billing consultando o estoque na hora de gerar ou aprovar** (fase 4): Rejeitado porque exigiria chamada HTTP entre as APIs ou leitura do banco da Management (ADR-020). O resultado vai no evento `OrdemServicoValidada`.
5. **Debitar ao aprovar o orçamento, antes do pagamento** (como na versão original): Substituído na entrada do Billing: a OS só entra em execução com o pagamento aprovado, e o débito acompanha esse momento.

**Relação com Outras ADRs**:
- **ADR-002**: Entidades ricas com lógica de negócio encapsulada.
- **ADR-004**: Portas de saída (Gateways) de persistência no projeto Application (UseCases) com implementação no projeto DB.
- **ADR-005**: Casos de uso e contratos segregados no projeto Application (UseCases).
- **ADR-020**: a verificação vai ao Billing no `OrdemServicoValidada`, e o débito acontece no `PagamentoAprovado`: [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md).
- **ADR-021** e **ADR-022** (repo BILLING): o orçamento e o pagamento: [ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md) e [ADR-022](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-022-Pagamentos-com-Mercado-Pago.md).
