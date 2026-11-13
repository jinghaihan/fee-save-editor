"""Read and update the catalogs embedded by the editor."""
import gzip
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "internal/catalog/data"

def load_catalog(name):
    return json.loads(gzip.decompress((ROOT / f"{name}.json.gz").read_bytes()))

def write_catalog(name, data):
    payload = (json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n").encode()
    (ROOT / f"{name}.json.gz").write_bytes(gzip.compress(payload, mtime=0))
