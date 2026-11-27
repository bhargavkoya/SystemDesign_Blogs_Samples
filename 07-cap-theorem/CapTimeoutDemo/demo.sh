#!/usr/bin/env bash
# Usage: ./demo.sh   (server must be running on http://localhost:5080)
B=http://localhost:5080
req() { # method path [json]
  local m=$1 p=$2 d=${3:-}
  if [ -n "$d" ]; then
    curl -s -o /tmp/body -w "%{http_code} %{time_total}s" -X "$m" -H 'Content-Type: application/json' -d "$d" "$B$p"
  else
    curl -s -o /tmp/body -w "%{http_code} %{time_total}s" -X "$m" "$B$p"
  fi
  echo "  $m $p"; cat /tmp/body; echo; echo
}
echo "== 1. Healthy: write 5, then read through each operation"
req POST /admin/write/sku-1 '{"value":"5"}'
req GET  /read/stock/sku-1
req GET  /read/catalog/sku-1
echo "== 2. Cut the link, then write 4 (Node A accepts, Node B never hears)"
req POST /admin/partition
req POST /admin/write/sku-1 '{"value":"4"}'
echo "== 3. Same key, same partition, three operations"
req GET  /read/stock/sku-1
req GET  /read/catalog/sku-1
req GET  /read/report/sku-1
echo "== 4. Heal: replay what Node B missed, then read again"
req POST /admin/heal
req GET  /read/catalog/sku-1
req GET  /read/stock/sku-1
