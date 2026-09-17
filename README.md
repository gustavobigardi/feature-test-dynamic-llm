# Como você testa uma feature que responde diferente toda vez?

**Data & AI Saturday Vitória 2026** · Gustavo Bigardi

> Você trocou o modelo. Ajustou três palavras no prompt. Todos os testes passaram verde. E o produto piorou.
>
> Nesta sessão construiremos um gate de qualidade de IA dentro de um pipeline .NET real: dataset dourado versionado (incluindo os casos adversariais), avaliadores determinísticos e LLM-as-judge, calibração do juiz, threshold sabendo que score é distribuição e não número, e como não estourar a fatura de token rodando eval em todo pull request. Tudo com `dotnet test`, xUnit e GitHub Actions.

## Conteúdo versionado

| Pasta/arquivo | O que é |
|---|---|
| [demo/](demo) | Código da demo: assistente .NET, portão de qualidade em xUnit e GitHub Actions |
| [.github/workflows/](.github/workflows) | Workflows de CI e do portão de qualidade de IA |
| [slides.pdf](slides.pdf) | Slides exportados da palestra |

O roteiro, o runbook, o template e os arquivos-fonte usados para gerar os slides ficam em `palestra/` localmente, mas essa pasta é ignorada pelo Git e não faz parte do repositório publicado.

## A história em uma frase

A suíte de testes cobre a parte determinística do sistema; a resposta do modelo fica com "alguém olha e acha que tá bom". O portão de qualidade de IA transforma isso em um check do PR: **a mudança piorou o produto** quando um caso crítico falha **ou** quando o intervalo de confiança da diferença entre o PR e o main fica inteiro abaixo da margem tolerada. Tudo medido por avaliadores determinísticos e por um juiz calibrado contra rótulos humanos.

