# meus-investimentos

## Teste de ponta a ponta (caixa-preta)

```
docker compose -f compose.test.yml up --build --exit-code-from tests
docker compose -f compose.test.yml down -v
```

Sobe `db` (Postgres vazio), `fakeapi` (nginx reproduzindo a fixture real de preços em
`tests/e2e/fixtures/precos-2026-09-25.http`) e `web` (a mesma imagem do `Dockerfile`); o
serviço `tests` roda `tests/e2e/run.sh`, que importa `tests/e2e/operacoes.csv`, reimporta,
sincroniza os preços e confere os totais em `GET /carteira`. Sai com código ≠ 0 em qualquer
falha.
