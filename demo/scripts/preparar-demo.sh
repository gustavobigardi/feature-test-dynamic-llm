#!/usr/bin/env bash
# Cria, a partir do main, as branches usadas na demo da palestra (só local, sem push):
#   demo/tres-palavras  troca três palavras do prompt ("tom mais acolhedor")
#   demo/troca-modelo   troca o modelo do assistente por um mais barato (MODELO_BARATO, padrão gpt-5-nano)
set -euo pipefail

raiz="$(git rev-parse --show-toplevel)"
cd "$raiz"

if [ -n "$(git status --porcelain)" ]; then
  echo "Há mudanças não commitadas. Faça commit ou stash antes." >&2
  exit 1
fi

origem="$(git rev-parse --abbrev-ref HEAD)"
modelo_barato="${MODELO_BARATO:-gpt-5-nano}"

tres_palavras() {
  perl -pi -e 's/cordial e objetivo/cordial e otimista/; s/Use somente as informações/Use preferencialmente as informações/; s/Seja transparente sobre custos/Seja gentil sobre custos/' demo/ia/prompt.md
  grep -q "cordial e otimista" demo/ia/prompt.md
  grep -q "Use preferencialmente as informações" demo/ia/prompt.md
  grep -q "Seja gentil sobre custos" demo/ia/prompt.md
}

troca_modelo() {
  perl -pi -e "s/\"modelo\": \"[^\"]+\"/\"modelo\": \"$modelo_barato\"/" demo/ia/assistente.json
  grep -q "\"modelo\": \"$modelo_barato\"" demo/ia/assistente.json
}

criar() {
  local branch="$1" mensagem="$2" mudanca="$3"
  git switch --quiet -C "$branch" main
  "$mudanca"
  git commit --quiet -am "$mensagem"
  echo "✔ $branch: $mensagem"
  git --no-pager diff --word-diff=color main "$branch" -- demo/ia | grep -E '\[-|\{\+|\x1b\[3[12]m' || true
  echo
}

criar demo/tres-palavras "Assistente: tom mais acolhedor (pedido do time de CX)" tres_palavras
criar demo/troca-modelo "Assistente: modelo mais barato ($modelo_barato)" troca_modelo

git switch --quiet "$origem"

cat <<'EOF'
Próximos passos (na hora da demo):
  git push -u origin demo/tres-palavras
  gh pr create --base main --head demo/tres-palavras --title "Assistente: tom mais acolhedor" --body "Três palavras no prompt."
EOF
