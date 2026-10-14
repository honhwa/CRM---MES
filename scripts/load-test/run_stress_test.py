#!/usr/bin/env python3
"""Stress test (14/10/2026), seguito del primo test di carico (run_load_test.py).
Copre quello che il primo giro aveva esplicitamente lasciato fuori: concorrenza più alta,
scritture concorrenti (non solo letture), un dataset di partenza più grande (non solo i 3
record di seed). Stessa API locale + branch Neon di prova isolato, nessun dato reale toccato.
"""
import argparse
import json
import statistics
import threading
import time
import urllib.error
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor, as_completed

LOCK = threading.Lock()


def req(base, method, path, body=None, token=None, timeout=20):
    url = base + path
    data = json.dumps(body).encode("utf-8") if body is not None else None
    hdrs = {"Content-Type": "application/json"} if data is not None else {}
    if token:
        hdrs["Authorization"] = f"Bearer {token}"
    r = urllib.request.Request(url, data=data, method=method, headers=hdrs)
    start = time.perf_counter()
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            raw = resp.read()
            elapsed = time.perf_counter() - start
            ctype = resp.headers.get("Content-Type", "")
            parsed = json.loads(raw) if raw and "application/json" in ctype else raw
            return resp.status, parsed, elapsed
    except urllib.error.HTTPError as e:
        raw = e.read()
        elapsed = time.perf_counter() - start
        ctype = e.headers.get("Content-Type", "")
        parsed = json.loads(raw) if raw and "application/json" in ctype else raw
        return e.code, parsed, elapsed
    except Exception as e:
        elapsed = time.perf_counter() - start
        return None, str(e), elapsed


def uniq(prefix):
    return f"{prefix}-{uuid.uuid4().hex[:8]}"


def percentile(values, p):
    if not values:
        return float("nan")
    values = sorted(values)
    k = (len(values) - 1) * (p / 100)
    f, c = int(k), min(int(k) + 1, len(values) - 1)
    if f == c:
        return values[f]
    return values[f] + (values[c] - values[f]) * (k - f)


def seed_large(base, admin_token, n_materials, n_products):
    """Dataset di partenza più realistico: decine di materiali e prodotti, non solo 3 record."""
    material_codes = []
    for _ in range(n_materials):
        code = uniq("MAT")
        status, body, _ = req(base, "POST", "/api/materials",
                               {"code": code, "name": f"Materiale {code}", "unit": "pz", "stock": 1000, "minStock": 10},
                               token=admin_token)
        if status not in (200, 201):
            raise RuntimeError(f"seed materiale fallito: {status} {body}")
        material_codes.append(code)

    product_ids = []
    for _ in range(n_products):
        code = uniq("PROD")
        status, body, _ = req(base, "POST", "/api/products",
                               {"code": code, "name": f"Prodotto {code}", "description": None}, token=admin_token)
        if status not in (200, 201):
            raise RuntimeError(f"seed prodotto fallito: {status} {body}")
        product_ids.append(body["id"])

    status, body, _ = req(base, "POST", "/api/customers",
                           {"name": "Cliente di prova", "code": uniq("CUST"), "email": None, "phone": None,
                            "address": None, "notes": None}, token=admin_token)
    if status not in (200, 201):
        raise RuntimeError(f"seed cliente fallito: {status} {body}")

    return material_codes, product_ids


def login_once(base, email, password):
    status, body, elapsed = req(base, "POST", "/api/auth/login", {"email": email, "password": password})
    token = body.get("token") if isinstance(body, dict) else None
    return status, elapsed, token


def read_iterations(base, token, iterations):
    endpoints = [
        ("GET", "/health", None, False, "Health check"),
        ("GET", "/api/materials", None, True, "Lista materiali"),
        ("GET", "/api/products", None, True, "Lista prodotti"),
        ("GET", "/api/work-orders", None, True, "Lista commesse"),
    ]
    results = []
    for _ in range(iterations):
        for method, path, payload, needs_token, label in endpoints:
            status, _, elapsed = req(base, method, path, payload, token=token if needs_token else None)
            results.append((label, status, elapsed))
    return results


def write_work_orders(base, token, product_id, count):
    results = []
    for _ in range(count):
        status, _, elapsed = req(base, "POST", "/api/work-orders",
                                  {"productId": product_id, "quantity": 10, "code": uniq("WO"), "areaId": None,
                                   "customerReference": None, "dueDate": None, "notes": None}, token=token)
        results.append(("Crea commessa", status, elapsed))
    return results


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://localhost:5092")
    ap.add_argument("--concurrency", type=int, default=50, help="Sessioni utente concorrenti (letture)")
    ap.add_argument("--iterations-per-worker", type=int, default=15)
    ap.add_argument("--write-concurrency", type=int, default=20, help="Scritture concorrenti (crea commessa)")
    ap.add_argument("--writes-per-worker", type=int, default=5)
    ap.add_argument("--n-materials", type=int, default=50)
    ap.add_argument("--n-products", type=int, default=20)
    ap.add_argument("--summary-only", action="store_true")
    args = ap.parse_args()

    print(f"=== Stress test: seed {args.n_materials} materiali + {args.n_products} prodotti, "
          f"{args.concurrency} letture concorrenti x {args.iterations_per_worker} giri, "
          f"{args.write_concurrency} scritture concorrenti x {args.writes_per_worker} ===")

    admin_email = uniq("admin") + "@stress.local"
    admin_password = "Password123!Aa"
    status, body, _ = req(args.base, "POST", "/api/auth/register",
                           {"name": "Admin", "email": admin_email, "password": admin_password})
    if status != 200:
        raise RuntimeError(f"bootstrap admin fallito: {status} {body}")
    admin_token = body["token"]

    seed_start = time.perf_counter()
    material_codes, product_ids = seed_large(args.base, admin_token, args.n_materials, args.n_products)
    seed_elapsed = time.perf_counter() - seed_start
    print(f"Seed completato in {seed_elapsed:.1f}s: {len(material_codes)} materiali, {len(product_ids)} prodotti")

    by_label = {}
    errors_by_label = {}
    status_codes_by_label = {}

    def record(label, status, elapsed):
        by_label.setdefault(label, []).append(elapsed)
        status_codes_by_label.setdefault(label, {})
        status_codes_by_label[label][status] = status_codes_by_label[label].get(status, 0) + 1
        if status is None or status >= 400:
            errors_by_label[label] = errors_by_label.get(label, 0) + 1

    # Fase 1: login concorrenti (una sessione = un login, come nella realtà).
    wall_start = time.perf_counter()
    tokens = []
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        futures = [pool.submit(login_once, args.base, admin_email, admin_password) for _ in range(args.concurrency)]
        for fut in as_completed(futures):
            status, elapsed, token = fut.result()
            with LOCK:
                record("Login", status, elapsed)
            if token:
                tokens.append(token)

    if not tokens:
        raise RuntimeError("Nessun login riuscito: impossibile proseguire")

    # Fase 2: letture concorrenti, concorrenza più alta del primo giro.
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        futures = [pool.submit(read_iterations, args.base, tok, args.iterations_per_worker) for tok in tokens]
        for fut in as_completed(futures):
            for label, status, elapsed in fut.result():
                with LOCK:
                    record(label, status, elapsed)

    # Fase 3: scritture concorrenti (il primo giro aveva misurato solo letture).
    write_tokens = tokens[: args.write_concurrency] if len(tokens) >= args.write_concurrency else tokens
    product_id = product_ids[0]
    with ThreadPoolExecutor(max_workers=len(write_tokens)) as pool:
        futures = [pool.submit(write_work_orders, args.base, tok, product_id, args.writes_per_worker)
                   for tok in write_tokens]
        for fut in as_completed(futures):
            for label, status, elapsed in fut.result():
                with LOCK:
                    record(label, status, elapsed)

    wall_elapsed = time.perf_counter() - wall_start
    total_requests = sum(len(v) for v in by_label.values())
    total_errors = sum(errors_by_label.values())
    throughput = total_requests / wall_elapsed if wall_elapsed > 0 else 0

    print(f"\nDurata (login+letture+scritture): {wall_elapsed:.2f}s — {total_requests} richieste — "
          f"throughput {throughput:.1f} req/s")
    print(f"{'Rotta':<20} {'n':>6} {'errori':>7} {'p50 ms':>9} {'p90 ms':>9} {'p99 ms':>9} {'max ms':>9}")
    for label, values in by_label.items():
        ms = [v * 1000 for v in values]
        errs = errors_by_label.get(label, 0)
        print(f"{label:<20} {len(values):>6} {errs:>7} {percentile(ms, 50):>9.1f} {percentile(ms, 90):>9.1f} "
              f"{percentile(ms, 99):>9.1f} {max(ms):>9.1f}")

    error_rate = (total_errors / total_requests * 100) if total_requests else 0
    print(f"\nTasso di errore complessivo: {error_rate:.2f}% ({total_errors}/{total_requests})")
    print("\nCodici di stato per rotta (solo rotte con almeno un errore):")
    for label, codes in status_codes_by_label.items():
        if any(c is None or c >= 400 for c in codes):
            print(f"  {label}: {codes}")

    if error_rate > 1.0:
        print("\nESITO: soglia di errore superata (>1%) — vedi dettaglio sopra.")
    else:
        print("\nSTRESS_TEST_SUMMARY_OK")


if __name__ == "__main__":
    main()
