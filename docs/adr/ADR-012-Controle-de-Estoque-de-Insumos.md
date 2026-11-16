# ADR-012: Controle de Estoque de Insumos

**Status**: Aceita (atualizada na fase 4: o orçamento e o estoque continuam na Management, nas rotas `/api/orcamentos/{ordemServicoId}`, e a OS mudou para o OS Service, ver [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md) e [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md))

**Contexto**:
O sistema de oficina gerencia ordens de serviço que consomem insumos (peças, óleos, filtros, etc.). Cada serviço possui itens que referenciam insumos com quantidade necessária. Atualmente, não há controle de disponibilidade de estoque ao gerar orçamentos ou ao aprovar e iniciar a execução de ordens de serviço, podendo resultar em orçamentos inviáveis ou execução sem recursos.

**Decisão**:

1. **Verificação ao Gerar Orçamento** (`POST /api/orcamentos/{ordemServicoId}/gerar`):
   - Antes de gerar o orçamento, o sistema deve verificar se há estoque suficiente de todos os insumos necessários.
   - A verificação é apenas informativa, **sem reservar** os insumos.
   - Se houver estoque insuficiente, lança `DomainException` impedindo a geração do orçamento.

2. **Débito ao Aprovar Orçamento** (`POST /api/orcamentos/{ordemServicoId}/aprovar`):
   - Ao aprovar o orçamento, o sistema deve **debitar automaticamente** a quantidade de insumos do estoque.
   - Antes de debitar, o sistema verifica novamente a disponibilidade (o estoque pode ter mudado entre geração e aprovação).
   - Se houver estoque insuficiente no momento da aprovação, lança `DomainException` impedindo a aprovação.
   - Desde a fase 4, o débito acontece na Management, e a mudança de status da OS para `EmExecucao` acontece depois, no OS Service, quando ele consome o evento `OrcamentoAprovado` (ADR-020).

3. **Sem Sistema de Reservas**:
   - Não há reserva de insumos entre a geração do orçamento e a aprovação.
   - O controle é baseado em verificação pontual da quantidade disponível no momento da operação.

4. **Caso de Uso de Estoque**:
	- Interface `IEstoqueUseCases` no projeto **Application (UseCases)** (contrato da orquestração de estoque).
	- Implementação `EstoqueUseCases` no projeto **Application (UseCases)**, consumida pelo fluxo de orçamento (`OrcamentoUseCases`) via `IOrcamentoUseCasesFacade`. Ela recebe os serviços da OS, carregados a partir dos ids guardados na referência da OS.
	- Justificativa: a lógica cruza múltiplos insumos e requer acesso a portas de saída (Gateways), não cabendo em uma única entidade.

5. **Métodos na Entidade `Insumo`**:
   - `VerificarDisponibilidade(int quantidadeNecessaria)`: valida se há estoque >= quantidade, lança exceção se insuficiente.
   - `DebitarEstoque(int quantidade)`: valida e decrementa `QuantidadeDisponivel`, lança exceção se insuficiente.
   - Mantém lógica de negócio no domínio (entidades ricas, ADR-002).

6. **Sem Auditoria de Movimentação**:
   - Não há necessidade de histórico de movimentações de estoque (entrada, saída, reserva).
   - Apenas atualiza a propriedade `QuantidadeDisponivel` do insumo.

**Consequências**:

- ✅ Garante que orçamentos gerados são viáveis em termos de estoque.
- ✅ Impede início de execução sem recursos disponíveis.
- ✅ Mantém lógica de negócio no domínio (DDD).
- ✅ Simplicidade: sem entidades de movimentação ou reserva.
- ⚠ Possível "race condition" entre geração e aprovação (aceito pelo negócio).
- ⚠ Orçamentos gerados podem ficar inviáveis se o estoque for consumido por outras ordens antes da aprovação.
- ⚠ Sem histórico de movimentações (aceito pelo negócio).

**Fluxo de Uso** (resumo de `OrcamentoUseCases`, na Management):

```csharp
// 1. Gerar Orçamento
public async Task<OrcamentoViewModel> GerarAsync(GerarOrcamentoCommand command, CancellationToken cancellationToken = default)
{
	var referencia = await ObterReferenciaExistenteAsync(command.OrdemServicoId, cancellationToken);
	referencia.ValidarGeracaoOrcamento();
	// ...
	var servicos = await ObterServicosAsync(referencia.ServicoIds, cancellationToken);

	// Verifica estoque (sem reservar)
	await _facade.EstoqueUseCases.VerificarDisponibilidadeParaOrcamentoAsync(servicos, cancellationToken);

	// Cria (ou gera de novo) o orçamento, grava e publica OrcamentoGerado para o OS Service
	// ...
}

// 2. Aprovar Orçamento
public async Task<OrcamentoViewModel> AprovarAsync(AprovarOrcamentoCommand command, CancellationToken cancellationToken = default)
{
	var orcamento = await ObterOrcamentoExistenteAsync(command.OrdemServicoId, cancellationToken);
	orcamento.ValidarAprovacao();

	var referencia = await ObterReferenciaExistenteAsync(orcamento.OrdemServicoId, cancellationToken);
	referencia.ValidarAprovacaoOrcamento();

	var servicos = await ObterServicosAsync(referencia.ServicoIds, cancellationToken);

	// Verifica novamente (estoque pode ter mudado)
	await _facade.EstoqueUseCases.VerificarDisponibilidadeParaOrcamentoAsync(servicos, cancellationToken);

	// Debita do estoque
	await _facade.EstoqueUseCases.DebitarEstoqueParaOrdemServicoAsync(servicos, cancellationToken);

	orcamento.Aprovar();
	await _orcamentoGateway.AtualizarAsync(orcamento, cancellationToken);

	// O OS Service leva a OS para EmExecucao ao consumir este evento (ADR-020)
	await _facade.EventosPublisher.PublicarAsync(new OrcamentoAprovado(orcamento.OrdemServicoId), orcamento.OrdemServicoId, cancellationToken);

	return _mapper.Map<OrcamentoViewModel>(orcamento);
}
```

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

**Relação com Outras ADRs**:
- **ADR-002**: Entidades ricas com lógica de negócio encapsulada.
- **ADR-004**: Portas de saída (Gateways) de persistência no projeto Application (UseCases) com implementação no projeto DB.
- **ADR-005**: Casos de uso e contratos segregados no projeto Application (UseCases).
- **ADR-020**: o resultado do orçamento chega ao OS Service por evento (`OrcamentoGerado`, `OrcamentoAprovado` e `OrcamentoRecusado`): [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md).
