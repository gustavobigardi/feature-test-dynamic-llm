#!/usr/bin/env bash
# Roda o portão de qualidade de IA na sua máquina: working tree (candidato) contra uma ref (baseline).
# Uso: scripts/avaliar.sh [smoke|completo] [ref-da-baseline]
set -euo pipefail

nivel="${1:-smoke}"
base="${2:-origin/main}"
raiz="$(git rev-parse --show-toplevel)"
baseline="$raiz/.baseline"
demo="$raiz/demo"

if [ -z "${AZURE_OPENAI_ENDPOINT:-}" ]; then
  echo "Defina AZURE_OPENAI_ENDPOINT (e AZURE_OPENAI_API_KEY ou faça az login)." >&2
  exit 1
fi

git -C "$raiz" worktree remove --force "$baseline" 2>/dev/null || true
git -C "$raiz" worktree add --detach "$baseline" "$base" >/dev/null
trap 'git -C "$raiz" worktree remove --force "$baseline"' EXIT

echo "Baseline: $base ($(git -C "$baseline" rev-parse --short HEAD)) · candidato: working tree · nível: $nivel"

(
  cd "$demo"
  EVAL_NIVEL="$nivel" EVAL_BASELINE_DIR="$baseline/demo" \
    dotnet test --project tests/ConectaSuporte.Evals -- --filter-trait "etapa=portao"
)
