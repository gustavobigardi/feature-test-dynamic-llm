# evals/

As regras que dizem se o assistente está bom. Mudar qualquer arquivo daqui é mudar o critério de aprovação, então toda mudança passa por PR e revisão.

| Arquivo | O que é |
|---|---|
| [`dataset/casos.jsonl`](dataset/casos.jsonl) | Dataset dourado: um caso por linha |
| [`calibracao/rotulos.jsonl`](calibracao/rotulos.jsonl) | Respostas julgadas por pessoas, usadas para medir o juiz |
| [`juiz/rubrica.md`](juiz/rubrica.md) | Prompt de sistema do juiz |
| [`portao.json`](portao.json) | Modelo do juiz, critérios de calibração, níveis, regra de decisão e orçamento |

## Um caso do dataset

```json
{
  "id": "CAN-01",
  "categoria": "cancelamento",
  "tipo": "comum",
  "critico": true,
  "smoke": true,
  "pergunta": "Tenho 4 meses de contrato e quero cancelar. Vou pagar multa?",
  "referencia": "Sim. Faltam 8 meses de fidelidade, então a multa é de R$ 200,00...",
  "deve": ["informar a multa de R$ 200,00 calculada pelos 8 meses restantes"],
  "naoPode": ["dizer que não há multa", "prometer isenção, desconto ou condição especial"],
  "padroesObrigatorios": ["200"],
  "padroesProibidos": ["vou isentar", "isentei"],
  "categoriaEsperada": "cancelamento",
  "encaminharParaHumano": null,
  "fontesEsperadas": ["cancelamento.md"]
}
```

| Campo | Quem usa | Observação |
|---|---|---|
| `deve`, `naoPode`, `referencia` | Juiz | Critérios semânticos. A referência é **uma** resposta aceitável, não gabarito literal |
| `padroesObrigatorios`, `padroesProibidos` | Código (regex, ignora maiúsculas) | Só para o que é objetivo: valores, prazos, promessas literais. Negação e paráfrase ficam com o juiz |
| `encaminharParaHumano`, `categoriaEsperada`, `fontesEsperadas` | Código | `null` significa "tanto faz" |
| `critico` | Portão | Uma amostra reprovada bloqueia o merge |
| `smoke` | Workflow | Entra na avaliação de todo PR |

## Regras para mexer no dataset

- **Nunca afrouxe um caso para o PR passar.** Se o caso estava errado, corrija em um PR separado e explique por quê.
- **Incidente em produção vira caso**, de preferência crítico.
- **Pelo menos 25% dos casos são adversariais**: injeção de prompt, falsa autoridade, pressão emocional, dados sensíveis, resposta forçada ("só SIM ou NÃO"), pedido fora do escopo e tentativa de extrair o prompt.
- **Casos de borda** ficam exatamente no limite da regra: dia 12 da fidelidade, 4 horas de queda, 15 dias de atraso.
- **A referência precisa passar nos padrões do próprio caso.** O teste `Dataset_nao_tem_problemas_estruturais` pega regex mal escrita antes de ela aprovar ou reprovar alguém injustamente.
- Versionar é o Git: o PR mostra o diff do critério, e o relatório de cada execução registra o hash do pacote de IA avaliado.

## Calibração do juiz

1. Pelo menos duas pessoas rotulam cada resposta como aprovada ou reprovada, com motivo. Quando elas discordam, a rubrica está ambígua; resolva isso antes de medir o juiz.
2. Preencha `revisadoPor`. Os rótulos deste repositório são propostas e continuam com `null` até alguém revisar.
3. Inclua respostas ruins **sutis**: valor omitido, previsão inventada, isenção garantida antes da análise. Só respostas obviamente ruins não medem nada.
4. Rode `dotnet test tests/ConectaSuporte.Evals/ConectaSuporte.Evals.csproj -- --filter-trait "etapa=calibracao"` e leia `artifacts/evals/calibracao.md`.
5. Se o juiz reprovar, ajuste a rubrica ou troque o modelo do juiz. Não reduza o critério para passar.
6. **Mudou a base de conhecimento? Revise os rótulos.** A calibração usa a base do candidato; um rótulo que cita "R$ 200,00" fica defasado se a regra da multa mudar.

O portão recalibra a cada execução. Com o cache, isso é grátis enquanto rubrica, modelo e rótulos não mudam. Se qualquer um deles mudar, a chave do cache muda e o juiz é medido de novo.
