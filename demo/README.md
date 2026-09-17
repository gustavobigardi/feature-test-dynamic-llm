# Portão de qualidade de IA com dotnet test

Demo da palestra **"Como você testa uma feature que responde diferente toda vez?"** (Data & AI Saturday Vitória 2026).

O assistente de suporte da (fictícia) **Conecta Vix**, provedora de internet por fibra na Grande Vitória, responde clientes com Azure OpenAI. Os testes de sempre garantem o contrato da API. Este repositório adiciona o que falta: um **portão de qualidade de IA** que roda em `dotnet test`, com xUnit e GitHub Actions, e responde de forma objetiva se uma mudança de prompt, base de conhecimento ou modelo **piorou o produto**.

```
PR mexe em ia/ ──► CI: build + testes (0 tokens) ─────────────────────────────────────────► ✅
              └──► Portão de IA: calibra o juiz ─► N amostras × (main, PR) ─► avaliadores ─► bootstrap ─► ✅ ⛔ ❌ + comentário no PR
```

## Como funciona

| Pergunta | Implementação |
|---|---|
| O que muda o comportamento da IA? | [`ia/`](ia): [`prompt.md`](ia/prompt.md), [`assistente.json`](ia/assistente.json) (modelo) e [`conhecimento/`](ia/conhecimento). Tudo versionado; o relatório mostra o hash do pacote avaliado |
| O que é uma resposta boa? | [`evals/dataset/casos.jsonl`](evals/dataset/casos.jsonl): 32 casos comuns, de borda e adversariais, com o que a resposta **deve** e **não pode** fazer |
| O que o código consegue verificar? | [Avaliadores determinísticos](src/ConectaSuporte.Avaliacao/Avaliadores/AvaliadoresDeterministicos.cs): contrato, valores obrigatórios, promessas literais, encaminhamento, categoria, fontes, CPF/cartão e vazamento do prompt |
| E o que o código não alcança? | [`JuizDeQualidade`](src/ConectaSuporte.Avaliacao/Avaliadores/JuizDeQualidade.cs): LLM-as-judge com [rubrica](evals/juiz/rubrica.md). O juiz dá nota e evidência; o código decide |
| Por que confiar no juiz? | [Calibração](src/ConectaSuporte.Avaliacao/Calibracao/Calibracao.cs) contra [24 rótulos humanos](evals/calibracao/rotulos.jsonl) antes de cada avaliação: concordância, kappa de Cohen e zero falso aceite em caso crítico |
| Como afirmar que piorou? | Cada caso roda N vezes no main e no PR. Um bootstrap por caso gera o intervalo de confiança da diferença. Margem, taxa mínima e tolerância zero para casos críticos ficam em [`evals/portao.json`](evals/portao.json) |
| Como não estourar a fatura? | Escopo pelo diff, smoke em PR e completo sob demanda, cache de respostas em disco e orçamento de tokens que interrompe a execução |

A regra de decisão fica em [`PortaoDeQualidade.Decidir`](src/ConectaSuporte.Avaliacao/Portao/PortaoDeQualidade.cs):

1. **Falha crítica:** uma única amostra reprovada em caso crítico bloqueia, seja qual for a média.
2. **Piorou:** o intervalo de confiança da diferença fica inteiro abaixo da margem tolerada (−5 p.p.).
3. **Abaixo do mínimo:** a taxa de aprovação fica abaixo do piso absoluto (80%), mesmo sem piora em relação ao main.
4. **Aprovado:** o intervalo fica inteiro acima da margem. **Inconclusivo:** o intervalo cruza a margem; por padrão bloqueia e pede o nível completo.

## Estrutura

```
ia/                          o que muda o comportamento: prompt, modelo e base de conhecimento
evals/                       dataset dourado, rótulos de calibração, rubrica do juiz e regras do portão
src/ConectaSuporte/          a feature: AssistenteSuporte (IChatClient + saída estruturada)
src/ConectaSuporte.Api/      POST /perguntas
src/ConectaSuporte.Avaliacao/ avaliadores, juiz, calibração, bootstrap, cache, orçamento e relatório
tests/ConectaSuporte.Tests/  roda em todo PR, sem modelo: contrato, avaliadores, estatística, dataset e pipeline com modelo falso
tests/ConectaSuporte.Evals/  roda com modelo real: calibração do juiz e portão de qualidade
scripts/                     avaliar.sh (local) e preparar-demo.sh (branches da palestra)
../.github/workflows/        ci.yml e portao-ia.yml
```

## Pré-requisitos

- .NET SDK 10 (o `global.json` usa o Microsoft Testing Platform no `dotnet test`).
- Para os evals: recurso Azure OpenAI com duas implantações cujos nomes batem com os modelos configurados, `gpt-5-mini` (assistente, em [`ia/assistente.json`](ia/assistente.json)) e `gpt-5` (juiz, em [`evals/portao.json`](evals/portao.json)). A identidade que roda os evals precisa do papel **Cognitive Services OpenAI User** no recurso.
- Para a demo de troca de modelo: implantação `gpt-5-nano` (ou defina `MODELO_BARATO`).

## Rodar localmente

### Preparar o Azure OpenAI

Este é o passo a passo para executar a demo localmente depois de criar o recurso e as implantações:

1. Confirme que o recurso tem estas implantações, com estes nomes exatos:
   - `gpt-5-mini`: assistente, conforme `ia/assistente.json`.
   - `gpt-5`: juiz, conforme `evals/portao.json`.
   - `gpt-5-nano`: opcional, usado pela demonstração de troca de modelo.
2. No recurso Azure OpenAI, abra **Access control (IAM)** e selecione **Add role assignment**.
3. Escolha a role **Cognitive Services OpenAI User**.
4. Em **Members**, selecione a identidade que vai executar a demo. Para execução local, escolha sua conta de usuário; para o GitHub Actions, escolha o service principal usado pelo OIDC.
5. Conclua em **Review + assign**. A propagação da permissão pode levar alguns minutos.
6. Copie o endpoint do recurso, no formato `https://<nome-do-recurso>.openai.azure.com`.

O código usa a API compatível com Azure OpenAI. Um endpoint de projeto do Microsoft Foundry (`*.services.ai.azure.com/api/projects/...`) não é aceito diretamente por esta versão da demo.

### Configurar e validar a máquina

Abra um terminal na raiz do repositório e valide as ferramentas:

```bash
dotnet --version             # deve ser 10.x; o global.json fixa 10.0.400
az login
az account show             # confirme a subscription e a conta corretas
```

Defina o endpoint para a sessão atual do terminal:

```bash
export AZURE_OPENAI_ENDPOINT="https://<nome-do-recurso>.openai.azure.com"
```

A autenticação preferida é Entra ID, usando a sessão do `az login`. Não é necessário configurar uma chave. Se a organização exigir chave, use a alternativa abaixo, sem commitar o valor:

```bash
export AZURE_OPENAI_API_KEY="<sua-chave>"
```

Faça primeiro a validação sem consumir tokens:

```bash
cd demo
dotnet test tests/ConectaSuporte.Tests/ConectaSuporte.Tests.csproj
```

Depois confirme a conexão e o acesso ao modelo do assistente com uma única chamada da API:

```bash
dotnet run --project src/ConectaSuporte.Api --urls http://localhost:5080
```

Em outro terminal, repita a definição de `AZURE_OPENAI_ENDPOINT` (e a chave, se usar) e execute:

```bash
curl -s http://localhost:5080/perguntas \
  -H 'Content-Type: application/json' \
  -d '{"texto":"Tenho 4 meses de contrato e quero cancelar. Vou pagar multa?"}'
```

A resposta deve ser JSON estruturado. Um `401` ou `403` normalmente indica login ausente ou role ainda não propagada; um `404` normalmente indica nome de implantação incorreto.

### Executar a avaliação da demo

Com a API encerrada (`Ctrl+C`), calibre primeiro o juiz. Esta etapa consome chamadas na implantação `gpt-5`:

```bash
cd demo
dotnet test tests/ConectaSuporte.Evals/ConectaSuporte.Evals.csproj -- \
  --filter-trait "etapa=calibracao"
```

Se a calibração passar, volte à raiz do repositório e rode o portão em nível `smoke`:

```bash
cd ..
demo/scripts/avaliar.sh smoke
```

Para comparar o working tree com `origin/main`, a referência precisa existir localmente:

```bash
git fetch origin main
demo/scripts/avaliar.sh completo origin/main
```

Os relatórios ficam em `demo/artifacts/evals/`. Para gerar o HTML:

```bash
cd demo
dotnet tool restore
dotnet aieval report --path artifacts/evals \
  --output artifacts/evals/relatorio.html --open
```

Os relatórios ficam em `artifacts/evals/`: `relatorio.md` (o mesmo texto do comentário do PR), `calibracao.md`, `resultado.json`, e as pastas `cache/` e `results/` do `Microsoft.Extensions.AI.Evaluation.Reporting`.

### Variáveis de ambiente dos evals

| Variável | Padrão | Uso |
|---|---|---|
| `AZURE_OPENAI_ENDPOINT` | — | Sem ela, os evals são pulados |
| `AZURE_OPENAI_API_KEY` | — | Sem ela, usa Entra ID (`az login` ou OIDC) |
| `EVAL_NIVEL` | `smoke` | `smoke` ou `completo` |
| `EVAL_BASELINE_DIR` | — | Checkout do main. Sem ele, valem só os critérios absolutos |
| `EVAL_ARTEFATOS` | `artifacts/evals` | Relatórios, resultados e cache |
| `EVAL_EXECUCAO` | data e hora | Nome da execução no `aieval report` |
| `EVAL_SEM_CACHE` | `false` | `true` para detectar drift do modelo |

## Níveis e custo

| Nível | Quando roda | Casos × amostras | Chamadas sem cache |
|---|---|---|---:|
| smoke | PR que muda `ia/`, `evals/`, `src/` ou o próprio portão | 18 × 3, nas duas versões | 108 gerações + 108 julgamentos + 24 de calibração = **240** |
| completo | Rótulo `eval:completo`, push no main, execução manual e semanal | 32 × 5, nas duas versões | 320 + 320 + 24 = **664** |

O cache muda bastante essa conta:

- A chave inclui mensagens, opções e modelo. Se o main não mudou, o lado baseline sai todo do cache.
- Reexecutar o mesmo PR sem mudanças custa zero chamadas.
- Uma resposta idêntica reaproveita o julgamento.
- Cada amostra tem a sua própria entrada no cache, então as N amostras continuam sendo N gerações diferentes.

O relatório mostra as chamadas reais e os tokens de entrada e de saída; multiplique pelo preço das suas implantações. O orçamento de tokens de cada nível fica em `evals/portao.json`, e estourá-lo interrompe a execução: **execução incompleta não é aprovação**.

A execução semanal roda **sem cache**, porque cache não detecta drift do modelo.

## GitHub Actions

| Workflow | Gatilho | O que faz |
|---|---|---|
| [ci](../.github/workflows/ci.yml) | todo PR e push no main | Build e `tests/ConectaSuporte.Tests` (0 tokens) |
| [portao-ia](../.github/workflows/portao-ia.yml) | PR, push no main, semanal, manual | Calcula o escopo pelo diff; se a IA foi afetada, cria a baseline com `git worktree`, roda `dotnet test` no projeto de evals, comenta o relatório no PR e publica HTML e JSON como artefato |

Configuração:

1. No Azure, crie um **App Registration** exclusivo para o GitHub Actions e seu service principal. Crie duas federated credentials, sem secret:
  - `repo:<owner>/<repo>:ref:refs/heads/main`
  - `repo:<owner>/<repo>:pull_request`
  Use o issuer `https://token.actions.githubusercontent.com` e a audience `api://AzureADTokenExchange`.
2. Atribua ao service principal a role **Cognitive Services OpenAI User** no recurso Azure OpenAI.
3. Em **Settings > Secrets and variables > Actions > Variables**, cadastre `AZURE_OPENAI_ENDPOINT`, `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` e `AZURE_SUBSCRIPTION_ID`. Não coloque client secret ou chave de API no código.
4. Crie o label `eval:completo`. Adicioná-lo a um PR muda a avaliação de `smoke` para `completo`.
5. Na proteção de `main`, exija os checks **`build e testes (sem modelo)`** e **`portao-ia`**. O job `portao-ia` sempre roda: avaliação pulada, cancelada ou incompleta não vira aprovação, e PR que não afeta a IA passa sem gastar tokens.
6. Proteja `demo/evals/`, `demo/tests/ConectaSuporte.Evals/` e `.github/workflows/` com CODEOWNERS. Quem edita o portão consegue afrouxá-lo; o YAML não substitui a revisão.

Para repetir a configuração com GitHub CLI, substitua os valores entre `<...>`:

```bash
gh variable set AZURE_OPENAI_ENDPOINT --body "https://<recurso>.openai.azure.com"
gh variable set AZURE_CLIENT_ID --body "<application-client-id>"
gh variable set AZURE_TENANT_ID --body "<tenant-id>"
gh variable set AZURE_SUBSCRIPTION_ID --body "<subscription-id>"
gh label create eval:completo --description "Executa a avaliação completa de qualidade de IA" --color 1D76DB
```

Depois de publicar o workflow, use **Actions > Portão de qualidade de IA > Run workflow** para uma execução manual. Escolha `smoke` para o primeiro teste; `completo` faz mais chamadas ao modelo. Em um PR que altera `demo/ia/`, o workflow também é acionado automaticamente.

PRs de forks não recebem credenciais. Um mantenedor revisa o código e roda a avaliação em uma branch interna.

## Decisões de projeto

- **A baseline roda junto, em vez de ser um número guardado.** O main e o PR são avaliados no mesmo run, com o mesmo juiz e o mesmo dataset, o que controla drift do modelo e mudanças no próprio dataset. O cache deixa o lado main quase de graça.
- **O dataset do PR vale para os dois lados.** Um caso novo adicionado no PR também é aplicado ao main.
- **Inconclusivo bloqueia**, porque "não deu para provar que não piorou" não é "não piorou". Isso é configurável em `decisao.inconclusivoBloqueia`.
- **Os rótulos de calibração são propostas até alguém revisar.** Os 24 exemplos vêm com `revisadoPor: null` e os relatórios avisam enquanto isso não muda.
- **Sem retry automático e sem média de juízes.** Repetir até ficar verde transforma o portão em sorteio.

## Adaptar para o seu produto

1. Troque `ia/` pelo seu prompt, modelo e base de conhecimento.
2. Escreva os casos com quem conhece o produto. Comece pelos incidentes reais e pelos adversariais. O teste `DatasetDouradoTests` exige pelo menos 25% de adversariais e todo caso crítico no smoke.
3. Rotule respostas boas e ruins, com revisor, e calibre o juiz **antes** de usá-lo.
4. Negocie margem, taxa mínima e orçamento **antes** de ver o resultado da primeira mudança.
