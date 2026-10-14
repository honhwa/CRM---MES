#!/usr/bin/env python3
"""Test di carico (14/10/2026) su API locale + branch Neon di prova vuoto e isolato.

Non e' un backtest funzionale (quelli esistenti coprono correttezza/sicurezza):
qui si misura la latenza sotto richieste concorrenti, non una singola richiesta
alla volta. Nessuna chiamata va verso produzione: solo verso --base (default
localhost), e i dati di seed sono generati qui, non letti da produzione.
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


def req(base, method, path, body=None, token=None, timeout=15):
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
    except Exception as e:  # timeout, connessione rifiutata, ecc.
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


def seed(base):
    """Crea i dati minimi per avere qualcosa da leggere sotto carico. Sequenziale, non misurato."""
    admin_email = uniq("admin") + "@load.local"
    admin_password = "Password123!Aa"
    status, body, _ = req(base, "POST", "/api/auth/register",
                           {"name": "Admin", "email": admin_email, "password": admin_password})
    if status != 200:
        raise RuntimeError(f"bootstrap admin fallito: {status} {body}")
    admin_token = body["token"]

    status, body, _ = req(base, "POST", "/api/materials",
                           {"code": uniq("MAT"), "name": "Materiale di prova", "unit": "pz",
                            "stock": 1000, "minStock": 10}, token=admin_token)
    if status not in (200, 201):
        raise RuntimeError(f"creazione materiale fallita: {status} {body}")

    status, body, _ = req(base, "POST", "/api/products",
                           {"code": uniq("PROD"), "name": "Prodotto di prova", "description": "Prodotto di prova"},
                           token=admin_token)
    if status not in (200, 201):
        raise RuntimeError(f"creazione prodotto fallita: {status} {body}")

    status, body, _ = req(base, "POST", "/api/customers",
                           {"name": "Cliente di prova", "code": uniq("CUST"), "email": uniq("cliente") + "@load.local"},
                           token=admin_token)
    if status not in (200, 201):
        raise RuntimeError(f"creazione cliente fallita: {status} {body}")

    return admin_email, admin_password, admin_token


ENDPOINTS = [
    ("GET", "/health", None, False, "Health check"),
    ("GET", "/api/materials", None, True, "Lista materiali"),
    ("GET", "/api/products", None, True, "Lista prodotti"),
    ("GET", "/api/customers", None, True, "Lista clienti"),
    ("GET", "/api/work-orders", None, True, "Lista commesse"),
    ("GET", "/api/layout/capa.new", None, True, "Layout CAPA (Strumento Layout)"),
]


def login_once(base):
    """Una sessione utente effettua un solo login, come nella realtà (non ad ogni richiesta).
    Misurato a parte, fuori dal ciclo di lettura, cosi' il limite di richieste sul login (20/min per IP,
    pensato contro il tentativo di indovinare una password, non contro il traffico normale) non si
    confonde con la capacita' di lettura dell'API sotto carico."""
    status, body, elapsed = req(base, "POST", "/api/auth/login", {"email": LOGIN_EMAIL, "password": LOGIN_PASSWORD})
    token = body.get("token") if isinstance(body, dict) else None
    return status, elapsed, token


def worker_iterations(base, token, iterations):
    """Una 'sessione utente' gia' loggata: N giri sulle rotte di lettura rappresentative."""
    results = []
    for _ in range(iterations):
        for method, path, payload, needs_token, label in ENDPOINTS:
            status, _, elapsed = req(base, method, path, payload, token=token if needs_token else None)
            results.append((label, status, elapsed))
    return results


LOGIN_EMAIL = None
LOGIN_PASSWORD = None


def main():
    global LOGIN_EMAIL, LOGIN_PASSWORD
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://localhost:5092")
    ap.add_argument("--concurrency", type=int, default=20, help="Sessioni utente concorrenti")
    ap.add_argument("--iterations-per-worker", type=int, default=10)
    ap.add_argument("--summary-only", action="store_true", help="Non stampa ogni singola richiesta")
    args = ap.parse_args()

    print(f"=== Test di carico: {args.concurrency} sessioni concorrenti x {args.iterations_per_worker} giri ===")
    admin_email, admin_password, admin_token = seed(args.base)
    LOGIN_EMAIL, LOGIN_PASSWORD = admin_email, admin_password

    by_label = {}
    errors_by_label = {}
    status_codes_by_label = {}

    def record(label, status, elapsed):
        by_label.setdefault(label, []).append(elapsed)
        status_codes_by_label.setdefault(label, {})
        status_codes_by_label[label][status] = status_codes_by_label[label].get(status, 0) + 1
        if status is None or status >= 400:
            errors_by_label[label] = errors_by_label.get(label, 0) + 1

    # Fase 1: ogni sessione concorrente fa un solo login (come nella realta').
    wall_start = time.perf_counter()
    tokens = []
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        futures = [pool.submit(login_once, args.base) for _ in range(args.concurrency)]
        for fut in as_completed(futures):
            status, elapsed, token = fut.result()
            with LOCK:
                record("Login", status, elapsed)
            if token:
                tokens.append(token)

    if not tokens:
        raise RuntimeError("Nessun login riuscito: impossibile proseguire con la fase di lettura")

    # Fase 2: ogni sessione gia' loggata fa N giri di lettura concorrenti.
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        futures = [pool.submit(worker_iterations, args.base, tok, args.iterations_per_worker) for tok in tokens]
        for fut in as_completed(futures):
            for label, status, elapsed in fut.result():
                with LOCK:
                    record(label, status, elapsed)

    wall_elapsed = time.perf_counter() - wall_start
    total_requests = sum(len(v) for v in by_label.values())
    total_errors = sum(errors_by_label.values())
    throughput = total_requests / wall_elapsed if wall_elapsed > 0 else 0

    print(f"\nDurata totale: {wall_elapsed:.2f}s — {total_requests} richieste — throughput {throughput:.1f} req/s")
    print(f"{'Rotta':<30} {'n':>6} {'errori':>7} {'p50 ms':>9} {'p90 ms':>9} {'p99 ms':>9} {'max ms':>9}")
    for label, values in by_label.items():
        ms = [v * 1000 for v in values]
        errs = errors_by_label.get(label, 0)
        print(f"{label:<30} {len(values):>6} {errs:>7} {percentile(ms, 50):>9.1f} {percentile(ms, 90):>9.1f} "
              f"{percentile(ms, 99):>9.1f} {max(ms):>9.1f}")

    error_rate = (total_errors / total_requests * 100) if total_requests else 0
    print(f"\nTasso di errore complessivo: {error_rate:.2f}% ({total_errors}/{total_requests})")
    print("\nCodici di stato per rotta (solo rotte con almeno un errore):")
    for label, codes in status_codes_by_label.items():
        if any(c is None or c >= 400 for c in codes):
            print(f"  {label}: {codes}")

    if error_rate > 1.0:
        print("\nESITO: soglia di errore superata (>1%) — vedi dettaglio sopra per la rotta in difficolta'.")
    else:
        print("\nLOAD_TEST_SUMMARY_OK")


if __name__ == "__main__":
    main()
