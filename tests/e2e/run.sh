#!/bin/sh
# Teste de ponta a ponta, caixa-preta: só HTTP contra o container web real,
# atrás de um fakeapi que reproduz as fixtures reais da API de preços e da
# API SGS do BCB (CDI). Não conhece Carteira.Web nem Carteira.Core por dentro.
set -eu

apk add --no-cache jq tzdata >/dev/null

BASE="http://web:8080"
CSV="/e2e/operacoes.csv"
CSV_EXTRA="/e2e/operacao-selic-extra.csv"
FIXTURE="/e2e/fixtures/precos-2026-09-25.http"
FIXTURE_CDI="/e2e/fixtures/cdi-2025-01.http"

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

echo "==> 1/5: POST /api/operacoes com linha fora do operacoes.csv"
op_novo='{"data":"01/08/2025","titular":"renato","codigo":"tesouro-selic-2029-03-01","titulo":"Tesouro Selic 2029","vencimento":"01/03/2029","tipo":"APLICACAO","quantidade":"1,0","preco_unitario":"15000,00","taxas":"0"}'
resp3=$(curl -sf -X POST -H "Content-Type: application/json" -d "$op_novo" "$BASE/api/operacoes")
importadas3=$(printf '%s' "$resp3" | jq -r '.importadas')
if [ "$importadas3" != "1" ]; then
    fail "POST /api/operacoes (linha nova): esperava importadas=1, veio: $resp3"
fi
echo "importadas=$importadas3 — ok"

echo "==> 2/5: GET /carteira reflete a nova quantidade (Selic 3,00) e o novo valor/custo"
# Custo total anterior (48.605,55, já conferido acima) + a nova aplicação (1,0 x 15.000,00).
custo2=$(jq -n '48605.553571428571 + 15000')
custo2_ptbr=$(formata_ptbr "$(printf '%.2f' "$custo2")")

# Valor = 3,0 (em vez de 2,0) × puVenda(selic-2029) + 3,25 × puVenda(ipca-mais-2035)
valor2=$(jq -n --arg s "$pu_selic" --arg i "$pu_ipca" '(3.0 * ($s | tonumber)) + (3.25 * ($i | tonumber))')
valor2_ptbr=$(formata_ptbr "$(printf '%.2f' "$valor2")")

html2=$(curl -sf "$BASE/carteira")
echo "$html2" | grep -qF "3,00" || fail "quantidade 3,00 (Selic 2029) não encontrada na página"
echo "$html2" | grep -qF -- "$valor2_ptbr" || fail "novo valor total ($valor2_ptbr) não encontrado na página"
echo "$html2" | grep -qF -- "$custo2_ptbr" || fail "novo custo total ($custo2_ptbr) não encontrado na página"
echo "quantidade 3,00, valor total ($valor2_ptbr) e custo total ($custo2_ptbr) — todos presentes"

echo "==> 3/5: POST /api/import reimportando a mesma linha (idempotência contra o CSV)"
resp4=$(curl -sf -F "arquivo=@$CSV_EXTRA;type=text/csv" "$BASE/api/import")
importadas4=$(printf '%s' "$resp4" | jq -r '.importadas')
jaExistentes4=$(printf '%s' "$resp4" | jq -r '.jaExistentes')
if [ "$importadas4" != "0" ] || [ "$jaExistentes4" != "1" ]; then
    fail "reimportação via CSV: esperava importadas=0 e jaExistentes=1, veio: $resp4"
fi
echo "importadas=$importadas4, jaExistentes=$jaExistentes4 — ok"

echo "==> 4/5: POST /api/operacoes com resgate de 100 (acima da posição) — 400"
op_resgate='{"data":"01/09/2025","titular":"renato","codigo":"tesouro-selic-2029-03-01","titulo":"Tesouro Selic 2029","vencimento":"01/03/2029","tipo":"RESGATE","quantidade":"100","preco_unitario":"14000,00","taxas":"0"}'
resp5=$(curl -s -w '\n%{http_code}' -X POST -H "Content-Type: application/json" -d "$op_resgate" "$BASE/api/operacoes")
status5=$(printf '%s' "$resp5" | tail -n1)
body5=$(printf '%s' "$resp5" | sed '$d')
if [ "$status5" != "400" ]; then
    fail "resgate de 100: esperava HTTP 400, veio $status5: $body5"
fi
erro5=$(printf '%s' "$body5" | jq -r '.erro')
printf '%s' "$erro5" | grep -qF "posição" || fail "resgate de 100: esperava erro contendo 'posição', veio: $erro5"
html5=$(curl -sf "$BASE/carteira")
echo "$html5" | grep -qF -- "$valor2_ptbr" || fail "resgate de 100 recusado, mas o valor total da tabela mudou"
echo "$html5" | grep -qF -- "$custo2_ptbr" || fail "resgate de 100 recusado, mas o custo total da tabela mudou"
echo "HTTP $status5, erro contendo 'posição', tabela inalterada — ok"

echo "==> 5/5: POST /api/operacoes com data de amanhã — 400"
amanha=$(TZ=America/Sao_Paulo date -d @$(($(date +%s) + 86400)) +%d/%m/%Y)
op_futuro=$(jq -n --arg data "$amanha" '{data:$data, titular:"renato", codigo:"tesouro-selic-2029-03-01", titulo:"Tesouro Selic 2029", vencimento:"01/03/2029", tipo:"APLICACAO", quantidade:"1,0", preco_unitario:"14000,00", taxas:"0"}')
status6=$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "Content-Type: application/json" -d "$op_futuro" "$BASE/api/operacoes")
if [ "$status6" != "400" ]; then
    fail "data de amanhã ($amanha): esperava HTTP 400, veio $status6"
fi
echo "data de amanhã ($amanha) recusada com HTTP $status6 — ok"

echo "==> cenário CDI 1/4: operação de hoje (para o benchmark ter ponto no dia corrente)"
# Sem índice e sem operação exatamente em "hoje", R12 não gera ponto nesse dia
# e a frase ficaria "sem dado" — o job diário só busca CDI de hoje-7..hoje-1.
hoje=$(TZ=America/Sao_Paulo date +%d/%m/%Y)
op_hoje=$(jq -n --arg data "$hoje" '{data:$data, titular:"renato", codigo:"tesouro-selic-2029-03-01", titulo:"Tesouro Selic 2029", vencimento:"01/03/2029", tipo:"APLICACAO", quantidade:"0,01", preco_unitario:"10000,00", taxas:"0"}')
resp7=$(curl -sf -X POST -H "Content-Type: application/json" -d "$op_hoje" "$BASE/api/operacoes")
importadas7=$(printf '%s' "$resp7" | jq -r '.importadas')
if [ "$importadas7" != "1" ]; then
    fail "operação de hoje: esperava importadas=1, veio: $resp7"
fi
echo "importadas=$importadas7 — ok"

echo "==> cenário CDI 2/4: POST /api/indices/backfill"
body_cdi=$(awk 'blank{print; next} /^[[:space:]]*$/{blank=1}' "$FIXTURE_CDI")
registros_esperados=$(printf '%s' "$body_cdi" | jq 'length')

resp8=$(curl -sf -X POST "$BASE/api/indices/backfill")
registros8=$(printf '%s' "$resp8" | jq -r '.registros')
if [ "$registros8" != "$registros_esperados" ]; then
    fail "POST /api/indices/backfill: esperava registros=$registros_esperados (itens de $FIXTURE_CDI), veio: $resp8"
fi
echo "registros=$registros8 (= itens da fixture) — ok"

# Simula a R11 (saldo CDI por titular) a partir das operações (operacoes.csv +
# as lançadas via /api/operacoes nesta execução) e da fixture do CDI. Só é
# válido porque, neste cenário, todo fluxo cai exatamente num dia da fixture
# (02/01 a 31/01/2025) ou depois do último dia da fixture — nenhum fluxo cai
# num dia sem índice dentro do intervalo da fixture.
cat > /tmp/cdi-calc.jq <<'JQ'
def toDate: split("/") | .[2] + .[1] + .[0];
def fluxo:
  (.quantidade | gsub(",";".") | tonumber) as $q |
  (.preco_unitario | gsub(",";".") | tonumber) as $p |
  (.taxas | gsub(",";".") | tonumber) as $t |
  if .tipo == "APLICACAO" then $q * $p + $t else -($q * $p - $t) end;

($csv | split("\n") | map(select(length > 0)) | .[1:] | map(split(";")) |
  map({data: .[0], tipo: .[5], quantidade: .[6], preco_unitario: .[7], taxas: .[8]})
) as $linhasCsv |
($linhasCsv + $extras) as $operacoes |
(reduce $operacoes[] as $o ({}; .[$o.data] = ((.[$o.data] // 0) + ($o | fluxo)))) as $flows |
($operacoes | map(.data | toDate) | min) as $deKey |
(reduce ($fixture[] | select((.data | toDate) >= $deKey)) as $r
    (0; . * (1 + ($r.valor | tonumber) / 100) + ($flows[$r.data] // 0))
) as $saldoAteFixture |
($fixture | map(.data | toDate) | max) as $ultimoDiaFixture |
(reduce ($flows | to_entries | map(select((.key | toDate) > $ultimoDiaFixture)) | sort_by(.key | toDate) | .[]) as $f
    ($saldoAteFixture; . + $f.value)
) as $saldoFinal |
$saldoFinal
JQ

echo "==> cenário CDI 3/4: X (R11) a partir de operacoes.csv e da fixture, conferido em GET /carteira"
x1=$(jq -n --rawfile csv "$CSV" --argjson extras "[$op_novo, $op_hoje]" --argjson fixture "$body_cdi" -f /tmp/cdi-calc.jq)
x1_ptbr=$(formata_ptbr "$(printf '%.2f' "$x1")")

html6=$(curl -sf "$BASE/carteira")
echo "$html6" | grep -qE '<polyline[^>]*stroke-dasharray="6,4"' || fail "polyline tracejada do CDI não encontrada no gráfico"
echo "$html6" | grep -qF "Se fosse CDI: R\$" || fail "frase 'Se fosse CDI: R\$' não encontrada na página"
echo "$html6" | grep -qF -- "$x1_ptbr" || fail "X esperado (R\$ $x1_ptbr, calculado pela R11) não encontrado na página"
echo "X = R\$ $x1_ptbr — polyline tracejada, frase e valor — todos presentes"

echo "==> cenário CDI 4/4: reimportação com operação antiga (02/01/2025) muda a frase"
op_antigo='{"data":"02/01/2025","titular":"renato","codigo":"tesouro-selic-2029-03-01","titulo":"Tesouro Selic 2029","vencimento":"01/03/2029","tipo":"APLICACAO","quantidade":"0,1","preco_unitario":"14000,00","taxas":"0"}'
resp9=$(curl -sf -X POST -H "Content-Type: application/json" -d "$op_antigo" "$BASE/api/operacoes")
importadas9=$(printf '%s' "$resp9" | jq -r '.importadas')
if [ "$importadas9" != "1" ]; then
    fail "operação antiga: esperava importadas=1, veio: $resp9"
fi

x2=$(jq -n --rawfile csv "$CSV" --argjson extras "[$op_novo, $op_hoje, $op_antigo]" --argjson fixture "$body_cdi" -f /tmp/cdi-calc.jq)
x2_ptbr=$(formata_ptbr "$(printf '%.2f' "$x2")")

if [ "$x1_ptbr" = "$x2_ptbr" ]; then
    fail "operação antiga: X não mudou (continua R\$ $x1_ptbr) — invalidação não recalculou ValorCdi"
fi

html7=$(curl -sf "$BASE/carteira")
echo "$html7" | grep -qF -- "$x2_ptbr" || fail "novo X (R\$ $x2_ptbr, após operação antiga) não encontrado na página"
echo "X mudou de R\$ $x1_ptbr para R\$ $x2_ptbr após reimportação com operação antiga — ok"

echo "TUDO OK"
