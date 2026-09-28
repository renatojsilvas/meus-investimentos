#!/bin/sh
# Teste de ponta a ponta, caixa-preta: só HTTP contra o container web real,
# atrás de um fakeapi que reproduz a fixture real da API de preços.
# Não conhece Carteira.Web nem Carteira.Core por dentro.
set -eu

apk add --no-cache jq >/dev/null

BASE="http://web:8080"
CSV="/e2e/operacoes.csv"
FIXTURE="/e2e/fixtures/precos-2026-09-25.http"

fail() {
    echo "FALHA: $1" >&2
    exit 1
}

echo "==> aguardando /health"
i=0
until curl -sf "$BASE/health" >/dev/null 2>&1; do
    i=$((i + 1))
    if [ "$i" -ge 90 ]; then
        fail "/health não respondeu em 90s"
    fi
    sleep 1
done
echo "ok"

echo "==> primeira importação"
resp1=$(curl -sf -F "arquivo=@$CSV;type=text/csv" "$BASE/api/import")
importadas1=$(printf '%s' "$resp1" | jq -r '.importadas')
if [ "$importadas1" != "5" ]; then
    fail "primeira importação: esperava importadas=5, veio: $resp1"
fi
echo "importadas=$importadas1 — ok"

echo "==> reimportação (idempotência)"
resp2=$(curl -sf -F "arquivo=@$CSV;type=text/csv" "$BASE/api/import")
importadas2=$(printf '%s' "$resp2" | jq -r '.importadas')
if [ "$importadas2" != "0" ]; then
    fail "reimportação: esperava importadas=0, veio: $resp2"
fi
echo "importadas=$importadas2 — ok"

echo "==> POST /api/prices/sync"
curl -sf -X POST "$BASE/api/prices/sync" >/dev/null || fail "POST /api/prices/sync falhou"
echo "ok"

echo "==> calculando valor esperado a partir da fixture"
body=$(awk 'blank{print; next} /^[[:space:]]*$/{blank=1}' "$FIXTURE")

pu_selic=$(printf '%s' "$body" | jq -r '.[] | select(.codigo=="tesouro-selic-2029-03-01") | .puVenda')
pu_ipca=$(printf '%s' "$body" | jq -r '.[] | select(.codigo=="tesouro-ipca-mais-2035-05-15") | .puVenda')

[ -n "$pu_selic" ] && [ "$pu_selic" != "null" ] || fail "puVenda de tesouro-selic-2029-03-01 não encontrado na fixture"
[ -n "$pu_ipca" ] && [ "$pu_ipca" != "null" ] || fail "puVenda de tesouro-ipca-mais-2035-05-15 não encontrado na fixture"

# Valor = 2,0 × puVenda(selic-2029) + 3,25 × puVenda(ipca-mais-2035)
valor=$(jq -n --arg s "$pu_selic" --arg i "$pu_ipca" '(2.0 * ($s | tonumber)) + (3.25 * ($i | tonumber))')
valor_2casas=$(printf '%.2f' "$valor")

# Formata em pt-BR: vírgula decimal, ponto a cada 3 dígitos na parte inteira.
formata_ptbr() {
    inteiro=${1%.*}
    fracao=${1#*.}
    sinal=""
    case "$inteiro" in
        -*) sinal="-"; inteiro=${inteiro#-} ;;
    esac
    invertido=$(printf '%s' "$inteiro" | rev)
    agrupado=$(printf '%s' "$invertido" | sed -E 's/([0-9]{3})/\1./g' | sed -E 's/\.$//')
    printf '%s%s,%s' "$sinal" "$(printf '%s' "$agrupado" | rev)" "$fracao"
}

valor_ptbr=$(formata_ptbr "$valor_2casas")
custo_ptbr="48.605,55"

echo "valor total esperado: $valor_ptbr"
echo "custo total esperado: $custo_ptbr"

echo "==> conferindo GET /carteira"
html=$(curl -sf "$BASE/carteira")

echo "$html" | grep -qF -- "$valor_ptbr" || fail "valor total ($valor_ptbr) não encontrado na página"
echo "$html" | grep -qF -- "$custo_ptbr" || fail "custo total ($custo_ptbr) não encontrado na página"
echo "$html" | grep -qF "sem preço" || fail "'sem preço' (tesouro-selic-2099-01-01, fora da fixture) não encontrado na página"
echo "$html" | grep -qF "Atenção" || fail "aviso de posição sem preço não encontrado na página"

echo "valor total ($valor_ptbr), custo total ($custo_ptbr), 'sem preço' e aviso — todos presentes"
echo "TUDO OK"
