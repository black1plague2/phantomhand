import sys
from pathlib import Path

TOOLS_DEMO_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLS_DEMO_DIR.parents[1]
HEALTHY_FIXTURE = REPO_ROOT / "contracts" / "fixtures" / "sessions" / "healthy"

sys.path.insert(0, str(TOOLS_DEMO_DIR))
