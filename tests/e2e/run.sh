#!/bin/sh
# Teste de ponta a ponta, caixa-preta: só HTTP contra o container web real,
# atrás de um fakeapi que reproduz as fixtures reais da API de preços e da
# API SGS do BCB (CDI). Não conhece Carteira.Web nem Carteira.Core por dentro.
set -eu

apk add --no-cache jq tzdata zip unzip >/dev/null

# O diário da fakeapi só passa a existir no cenário de ações (ver compose.test.yml).
rm -f /diario/cotahist-d.zip

BASE="http://web:8080"
CSV="/e2e/operacoes.csv"
CSV_EXTRA="/e2e/operacao-selic-extra.csv"
CSV_RF="/e2e/operacoes-renda-fixa.csv"
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
if [ "$importadas1" != "8" ]; then
    fail "primeira importação: esperava importadas=8, veio: $resp1"
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
custo_ptbr="58.655,55"

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
# Custo total anterior (58.655,55, já conferido acima) + a nova aplicação (1,0 x 15.000,00).
custo2=$(jq -n '58655.553571428571 + 15000')
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
  if .tipo != "APLICACAO" and .tipo != "RESGATE" then 0 else
  (.quantidade | gsub(",";".") | tonumber) as $q |
  (.preco_unitario | gsub(",";".") | tonumber) as $p |
  (.taxas | gsub(",";".") | tonumber) as $t |
  if .tipo == "APLICACAO" then $q * $p + $t else -($q * $p - $t) end
  end;

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

echo "==> renda fixa 1/6: total geral e ativos do backfill de preços antes do cenário"
# Total geral = linha "Valor" da tabela "Total geral" (a última da página), em pt-BR.
total_geral() {
    printf '%s' "$1" | tr -d '\n' | grep -oE '<td>Valor</td>[[:space:]]*<td>[^<]*</td>' | tail -n1 | sed -E 's/.*<td>([^<]*)<\/td>$/\1/'
}
ptbr_para_numero() { printf '%s' "$1" | tr -d '.' | tr ',' '.'; }

html_rf0=$(curl -sf "$BASE/carteira")
total_rf0=$(total_geral "$html_rf0")
[ -n "$total_rf0" ] || fail "total geral não encontrado na página antes do cenário de renda fixa"
ativos_antes=$(curl -sf -X POST "$BASE/api/prices/backfill" | jq -r '.ativos')
case "$ativos_antes" in ''|*[!0-9]*) fail "backfill de preços antes do cenário: .ativos não numérico ('$ativos_antes')" ;; esac
echo "total geral antes: $total_rf0, ativos do backfill: $ativos_antes"

echo "==> renda fixa 2/6: CSV de 10 colunas (CDB, CDB vencido, cofrinho) e reimportação do CSV principal"
resp_rf=$(curl -sf -F "arquivo=@$CSV_RF;type=text/csv" "$BASE/api/import")
importadas_rf=$(printf '%s' "$resp_rf" | jq -r '.importadas')
if [ "$importadas_rf" != "3" ]; then
    fail "CSV de renda fixa: esperava importadas=3, veio: $resp_rf"
fi
resp_old=$(curl -sf -F "arquivo=@$CSV;type=text/csv" "$BASE/api/import")
importadas_old=$(printf '%s' "$resp_old" | jq -r '.importadas')
if [ "$importadas_old" != "0" ]; then
    fail "reimportação do CSV principal: esperava importadas=0, veio: $resp_old"
fi
echo "importadas=$importadas_rf (10 colunas), importadas=$importadas_old (CSV principal) — ok"

echo "==> renda fixa 3/6: PU na data da aplicação (R4) e depois PU 1.047,32"
# A importação já gravou o PU da aplicação (R4 para RendaFixa): sem nenhum POST /api/precos o CDB vale 10.000,00.
curl -sf "$BASE/carteira" | tr -d '\n' | grep -qE 'CDB Teste 2027.*10\.000,00' || fail "10.000,00 não encontrado na linha do CDB logo após o import"
resp_pu=$(curl -sf -X POST -H "Content-Type: application/json" -d '{"codigo":"cdb-teste-2027-03","data":"15/01/2026","pu":"1047,32"}' "$BASE/api/precos") || fail "POST /api/precos (PU 1.047,32) falhou"
pu_gravado=$(printf '%s' "$resp_pu" | jq -r '.pu')
[ "$pu_gravado" = "1047,32" ] || fail "POST /api/precos: esperava pu=1047,32, veio: $resp_pu"

echo "==> renda fixa 4/6: carteira com 10.473,20 na linha do CDB e total subindo exatamente 15.973,20 (CDB 10.473,20 + vencido 5.000,00 + cofrinho 500,00)"
html_rf1=$(curl -sf "$BASE/carteira")
echo "$html_rf1" | tr -d '\n' | grep -qE 'CDB Teste 2027.*10\.473,20' || fail "10.473,20 não encontrado na linha do CDB"
total_rf1=$(total_geral "$html_rf1")
esperado_rf1=$(formata_ptbr "$(jq -n --arg t "$(ptbr_para_numero "$total_rf0")" '($t | tonumber) + 15973.20' | xargs printf '%.2f')")
[ "$total_rf1" = "$esperado_rf1" ] || fail "total geral: esperava $esperado_rf1 ($total_rf0 + 15.973,20), veio $total_rf1"
ativos_depois=$(curl -sf -X POST "$BASE/api/prices/backfill" | jq -r '.ativos')
[ "$ativos_depois" = "$ativos_antes" ] || fail "backfill de preços: ativos mudou de $ativos_antes para $ativos_depois (renda fixa entrou)"
echo "CDB 10.473,20, total $total_rf0 -> $total_rf1, backfill ativos=$ativos_depois — ok"

echo "==> renda fixa 5/6: aviso de vencido só no CDB vencido (o cofrinho e o CDB de 2027 ficam de fora)"
# A página mostra o aviso na tabela do titular e na consolidada: 2 ocorrências com 1 titular.
n_vencido=$(echo "$html_rf1" | grep -oF "venceu em 30/06/2025" | wc -l | tr -d ' ')
n_venceu=$(echo "$html_rf1" | grep -oF "venceu em" | wc -l | tr -d ' ')
[ "$n_vencido" = "2" ] && [ "$n_venceu" = "2" ] || fail "esperava 'venceu em 30/06/2025' 2 vezes (titular + consolidado) e nenhum outro aviso, vieram $n_vencido e $n_venceu"
echo "$html_rf1" | grep -qF "Cofrinho Teste" || fail "cofrinho não aparece na página"
echo "'venceu em 30/06/2025' no titular e no consolidado, nenhum outro aviso — ok"

echo "==> renda fixa 6/6: POST /api/precos com data de amanhã — 400"
pu_futuro=$(jq -n --arg data "$amanha" '{codigo:"cdb-teste-2027-03", data:$data, pu:"1050,00"}')
status_pu=$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "Content-Type: application/json" -d "$pu_futuro" "$BASE/api/precos")
[ "$status_pu" = "400" ] || fail "PU com data de amanhã ($amanha): esperava HTTP 400, veio $status_pu"
echo "HTTP $status_pu — ok"

echo "==> ações 1/6: POST /api/cotacoes/backfill (ITUB3 + HGLG11)"
mkdir -p /tmp/cotahist-a
unzip -p /e2e/fixtures/cotahist-a2025-recorte.zip > /tmp/cotahist-a/COTAHIST_A2025.TXT
# Posições do contrato: tipo 01 (1-2), data (3-10), TPMERC 010 (25-27), ticker (13-24), fechamento (109-121, 2 decimais).
# Saída: "AAAAMMDD fechamento_em_centavos", uma linha por pregão.
fechamentos() {
    awk -v t="$2" 'substr($0,1,2)=="01" && substr($0,25,3)=="010" {
        tk = substr($0,13,12); gsub(/ /,"",tk)
        if (tk == t) print substr($0,3,8), substr($0,109,13) + 0
    }' "$1" | sort -u
}
# A primeira operação de ação/FII do CSV é 03/03/2025: o backfill grava de lá em diante.
registros_itub=$(fechamentos /tmp/cotahist-a/COTAHIST_A2025.TXT ITUB3 | awk '$1 >= 20250303' | wc -l | tr -d ' ')
registros_hglg=$(fechamentos /tmp/cotahist-a/COTAHIST_A2025.TXT HGLG11 | awk '$1 >= 20250303' | wc -l | tr -d ' ')
registros_esperados_acoes=$((registros_itub + registros_hglg))
[ "$registros_esperados_acoes" -gt 0 ] || fail "recorte sem linhas de ITUB3/HGLG11"

total_ac0=$(total_geral "$(curl -sf "$BASE/carteira")")
resp_ac=$(curl -sf -X POST "$BASE/api/cotacoes/backfill") || fail "POST /api/cotacoes/backfill falhou"
ativos_ac=$(printf '%s' "$resp_ac" | jq -r '.ativos')
registros_ac=$(printf '%s' "$resp_ac" | jq -r '.registros')
[ "$ativos_ac" = "2" ] || fail "backfill de cotações: esperava ativos=2, veio: $resp_ac"
[ "$registros_ac" = "$registros_esperados_acoes" ] || fail "backfill de cotações: esperava registros=$registros_esperados_acoes (ITUB3 $registros_itub + HGLG11 $registros_hglg do recorte), veio: $resp_ac"
echo "ativos=$ativos_ac, registros=$registros_ac (= linhas do recorte) — ok"

echo "==> ações 2/6: GET /carteira com ITUB3 313,5 × fechamento e HGLG11 10 × fechamento do último pregão do recorte"
cents_ptbr() { formata_ptbr "$(awk -v c="$1" 'BEGIN { printf "%d.%02d", int(c / 100), c % 100 }')"; }
linha_ativo() {
    printf '%s' "$1" | awk -v RS='</tr>' -v t="$2" 'index($0, "<td>" t "</td>") { print; exit }' | sed -E 's/<[^>]*>/ /g' | tr -s ' \n\t' ' ' | sed -E 's/^ //'
}
fecho_itub=$(fechamentos /tmp/cotahist-a/COTAHIST_A2025.TXT ITUB3 | tail -n1 | cut -d' ' -f2)
fecho_hglg=$(fechamentos /tmp/cotahist-a/COTAHIST_A2025.TXT HGLG11 | tail -n1 | cut -d' ' -f2)
# Em décimos de centavo: 313,5 × centavos = 3135 × centavos / 10; 10 × centavos = 100 × centavos / 10.
dec_itub=$((3135 * fecho_itub))
dec_hglg=$((100 * fecho_hglg))
valor_itub=$(cents_ptbr $(((dec_itub + 5) / 10)))
valor_hglg=$(cents_ptbr $(((dec_hglg + 5) / 10)))
html_ac=$(curl -sf "$BASE/carteira")
linha_itub=$(linha_ativo "$html_ac" ITUB3)
linha_hglg=$(linha_ativo "$html_ac" HGLG11)
case "$linha_itub" in
    "ITUB3 313,50 8.550,00 $(cents_ptbr "$fecho_itub") $valor_itub "*) ;;
    *) fail "linha do ITUB3: esperava 'ITUB3 313,50 8.550,00 $(cents_ptbr "$fecho_itub") $valor_itub', veio: $linha_itub" ;;
esac
case "$linha_hglg" in
    "HGLG11 10,00 1.500,00 $(cents_ptbr "$fecho_hglg") $valor_hglg "*) ;;
    *) fail "linha do HGLG11: esperava 'HGLG11 10,00 1.500,00 $(cents_ptbr "$fecho_hglg") $valor_hglg', veio: $linha_hglg" ;;
esac
echo "ITUB3 313,50 × $(cents_ptbr "$fecho_itub") = $valor_itub; HGLG11 10,00 × $(cents_ptbr "$fecho_hglg") = $valor_hglg — ok"

echo "==> ações 3/6: total geral subiu exatamente a soma"
total_ac1=$(total_geral "$html_ac")
centavos_de() { awk -v v="$(ptbr_para_numero "$1")" 'BEGIN { printf "%d", v * 100 + (v < 0 ? -0.5 : 0.5) }'; }
# O total mostrado é o soma exata arredondada, então a conferência é em décimos de centavo
# com tolerância de 1 centavo (arredondamento do antes e do depois).
esperado_dec=$(( $(centavos_de "$total_ac0") * 10 + dec_itub + dec_hglg ))
obtido_dec=$(( $(centavos_de "$total_ac1") * 10 ))
dif=$((obtido_dec - esperado_dec)); [ "$dif" -ge 0 ] || dif=$((-dif))
[ "$dif" -le 10 ] || fail "total geral: esperava $total_ac0 + $valor_itub + $valor_hglg (±0,01), veio $total_ac1"
echo "total $total_ac0 -> $total_ac1 (+ $valor_itub + $valor_hglg) — ok"

echo "==> ações 4/6: POST /api/prices/sync grava o fechamento do diário"
# O job pede o COTAHIST_D de ontem e só aceita linhas dessa data: o recorte diário
# da fixture (23/10/2026) é regravado com a data de ontem e passa a existir na fakeapi.
ontem_iso=$(TZ=America/Sao_Paulo date -d @$(($(date +%s) - 86400)) +%Y%m%d)
ontem_arq=$(printf '%s' "$ontem_iso" | sed -E 's/^(....)(..)(..)$/\3\2\1/')
mkdir -p /tmp/cotahist-d
unzip -p /e2e/fixtures/cotahist-d-recorte.zip | LC_ALL=C sed -E "s/^01[0-9]{8}/01$ontem_iso/" > "/tmp/cotahist-d/COTAHIST_D$ontem_arq.TXT"
(cd /tmp/cotahist-d && zip -q /tmp/cotahist-d.zip "COTAHIST_D$ontem_arq.TXT")
cp /tmp/cotahist-d.zip /diario/cotahist-d.zip
curl -sf -X POST "$BASE/api/prices/sync" >/dev/null || fail "POST /api/prices/sync (diário) falhou"
fecho_d_itub=$(fechamentos "/tmp/cotahist-d/COTAHIST_D$ontem_arq.TXT" ITUB3 | cut -d' ' -f2)
fecho_d_hglg=$(fechamentos "/tmp/cotahist-d/COTAHIST_D$ontem_arq.TXT" HGLG11 | cut -d' ' -f2)
[ -n "$fecho_d_itub" ] && [ -n "$fecho_d_hglg" ] || fail "recorte diário sem ITUB3 ou HGLG11"
[ "$fecho_d_itub" != "$fecho_itub" ] && [ "$fecho_d_hglg" != "$fecho_hglg" ] || fail "fechamento do diário igual ao do anual: a conferência não distinguiria o sync"
valor_d_itub=$(cents_ptbr $(((3135 * fecho_d_itub + 5) / 10)))
valor_d_hglg=$(cents_ptbr $(((100 * fecho_d_hglg + 5) / 10)))
html_d=$(curl -sf "$BASE/carteira")
case "$(linha_ativo "$html_d" ITUB3)" in
    "ITUB3 313,50 8.550,00 $(cents_ptbr "$fecho_d_itub") $valor_d_itub "*) ;;
    *) fail "ITUB3 após o sync: esperava PU $(cents_ptbr "$fecho_d_itub") e valor $valor_d_itub, veio: $(linha_ativo "$html_d" ITUB3)" ;;
esac
case "$(linha_ativo "$html_d" HGLG11)" in
    "HGLG11 10,00 1.500,00 $(cents_ptbr "$fecho_d_hglg") $valor_d_hglg "*) ;;
    *) fail "HGLG11 após o sync: esperava PU $(cents_ptbr "$fecho_d_hglg") e valor $valor_d_hglg, veio: $(linha_ativo "$html_d" HGLG11)" ;;
esac
echo "fechamento de $ontem_iso gravado: ITUB3 $(cents_ptbr "$fecho_d_itub"), HGLG11 $(cents_ptbr "$fecho_d_hglg") — ok"

echo "==> ações 5/6: reimportação dos CSVs antigos"
for csv in "$CSV" "$CSV_EXTRA" "$CSV_RF"; do
    resp_re=$(curl -sf -F "arquivo=@$csv;type=text/csv" "$BASE/api/import")
    importadas_re=$(printf '%s' "$resp_re" | jq -r '.importadas')
    [ "$importadas_re" = "0" ] || fail "reimportação de $csv: esperava importadas=0, veio: $resp_re"
done
echo "importadas=0 nos três CSVs — ok"

echo "==> ações 6/6: DESDOBRAMENTO em ticker sem posição — 400"
op_desdobra='{"data":"02/01/2025","titular":"renato","codigo":"zzzz3","titulo":"ZZZZ3","vencimento":"","tipo":"DESDOBRAMENTO","quantidade":"900","preco_unitario":"","taxas":"0","classe":"ACAO"}'
resp_dd=$(curl -s -w '\n%{http_code}' -X POST -H "Content-Type: application/json" -d "$op_desdobra" "$BASE/api/operacoes")
status_dd=$(printf '%s' "$resp_dd" | tail -n1)
body_dd=$(printf '%s' "$resp_dd" | sed '$d')
[ "$status_dd" = "400" ] || fail "desdobramento sem posição: esperava HTTP 400, veio $status_dd: $body_dd"
printf '%s' "$body_dd" | jq -r '.erro' | grep -qF "sem posição" || fail "desdobramento sem posição: esperava erro contendo 'sem posição', veio: $body_dd"
echo "HTTP $status_dd, erro contendo 'sem posição' — ok"

echo "TUDO OK"
