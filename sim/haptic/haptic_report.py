"""S: haptic_report.py - summarize a fake_haptic.py --json-log for test assertions.

Reads one JSON object per line (as written by fake_haptic.py's --json-log) and
reports cue counts by type, min gap, max intensity, and any violation of the
safety caps in contracts/HAPTIC_PROTOCOL.md, at whichever of the two levels
the record was captured at:

- "cue"    records: our internal/legacy semantic envelope, intensity 0-1,
  duration_ms <= 1000 (contract's software-side caps).
- "device" records: the Electronics team's v1.1 wire format, intensity 0-255,
  duration_ms in [50, 400] (contract's v1.1 device-level caps).

Both kinds share the same sustained-rate (<=2/s) and min-gap checks.
"""

import json
import argparse
from pathlib import Path
from typing import Any, Dict, List, Optional
from collections import Counter, defaultdict
from dataclasses import dataclass, field

# Contract caps (contracts/HAPTIC_PROTOCOL.md "Safety and privacy" + v1.1 mapping table)
CUE_MAX_INTENSITY = 1.0
CUE_MAX_DURATION_MS = 1000
DEVICE_MIN_INTENSITY = 0
DEVICE_MAX_INTENSITY = 255
DEVICE_MIN_DURATION_MS = 50
DEVICE_MAX_DURATION_MS = 400
MAX_CUES_PER_SEC = 2
DEFAULT_MIN_GAP_MS = 800


@dataclass
class ReportResult:
    total: int = 0
    counts_by_cue: Dict[str, int] = field(default_factory=lambda: defaultdict(int))
    counts_by_kind: Dict[str, int] = field(default_factory=lambda: defaultdict(int))
    min_gap_ms: Optional[float] = None
    max_intensity_normalized: Optional[float] = None
    violations: List[str] = field(default_factory=list)

    def as_dict(self) -> Dict[str, Any]:
        return {
            "total": self.total,
            "counts_by_cue": dict(self.counts_by_cue),
            "counts_by_kind": dict(self.counts_by_kind),
            "min_gap_ms": self.min_gap_ms,
            "max_intensity_normalized": self.max_intensity_normalized,
            "violation_count": len(self.violations),
            "violations": self.violations,
        }


def load_records(json_log_path: Path) -> List[Dict[str, Any]]:
    records = []
    with open(json_log_path, "r", encoding="utf-8") as f:
        for line_no, line in enumerate(f, start=1):
            line = line.strip()
            if not line:
                continue
            try:
                records.append(json.loads(line))
            except json.JSONDecodeError as e:
                raise ValueError(f"{json_log_path}:{line_no}: invalid JSON: {e}") from e
    return records


def _normalized_intensity(rec: Dict[str, Any]) -> Optional[float]:
    kind = rec.get("kind", "cue")
    if kind == "device":
        raw = rec.get("intensity_255")
        return None if raw is None else raw / 255.0
    raw = rec.get("intensity")
    return raw


def analyze(records: List[Dict[str, Any]]) -> ReportResult:
    result = ReportResult(total=len(records))

    gaps: List[float] = []
    intensities: List[float] = []
    # For the sustained >2 cues/sec check: recv_ts_ms per record, sliding 1s window.
    timestamps_ms: List[float] = []

    for rec in records:
        kind = rec.get("kind", "cue")
        result.counts_by_kind[kind] += 1

        cue_label = rec.get("cue") or "(unlabeled)"
        result.counts_by_cue[cue_label] += 1

        gap = rec.get("gap_since_prev_ms")
        if gap is not None:
            gaps.append(gap)

        norm_intensity = _normalized_intensity(rec)
        if norm_intensity is not None:
            intensities.append(norm_intensity)

        ts = rec.get("recv_ts_ms")
        if ts is not None:
            timestamps_ms.append(ts)

        min_gap_ms = rec.get("min_gap_ms", DEFAULT_MIN_GAP_MS) or DEFAULT_MIN_GAP_MS

        # --- cap checks, level-appropriate ---
        if kind == "device":
            intensity_255 = rec.get("intensity_255")
            duration_ms = rec.get("duration_ms")
            if intensity_255 is not None and not (DEVICE_MIN_INTENSITY <= intensity_255 <= DEVICE_MAX_INTENSITY):
                result.violations.append(
                    f"device intensity {intensity_255} outside [{DEVICE_MIN_INTENSITY},{DEVICE_MAX_INTENSITY}] (cue_id={rec.get('cue_id')})"
                )
            if duration_ms is not None and not (DEVICE_MIN_DURATION_MS <= duration_ms <= DEVICE_MAX_DURATION_MS):
                result.violations.append(
                    f"device duration_ms {duration_ms} outside [{DEVICE_MIN_DURATION_MS},{DEVICE_MAX_DURATION_MS}] (cue_id={rec.get('cue_id')})"
                )
        else:
            intensity = rec.get("intensity")
            duration_ms = rec.get("duration_ms")
            if intensity is not None and intensity > CUE_MAX_INTENSITY:
                result.violations.append(f"cue intensity {intensity} > {CUE_MAX_INTENSITY} (id={rec.get('id')})")
            if duration_ms is not None and duration_ms > CUE_MAX_DURATION_MS:
                result.violations.append(f"cue duration_ms {duration_ms} > {CUE_MAX_DURATION_MS} (id={rec.get('id')})")

        if gap is not None and gap < min_gap_ms:
            result.violations.append(
                f"gap_since_prev_ms {gap:.1f} < min_gap_ms {min_gap_ms} (kind={kind}, id={rec.get('id') or rec.get('cue_id')})"
            )

    if gaps:
        result.min_gap_ms = min(gaps)
    if intensities:
        result.max_intensity_normalized = max(intensities)

    # Sustained rate check: any 1-second sliding window with > MAX_CUES_PER_SEC events.
    timestamps_ms.sort()
    n = len(timestamps_ms)
    left = 0
    for right in range(n):
        while timestamps_ms[right] - timestamps_ms[left] > 1000.0:
            left += 1
        window_count = right - left + 1
        if window_count > MAX_CUES_PER_SEC:
            result.violations.append(
                f"sustained rate: {window_count} cues within 1s window ending at ts_ms={timestamps_ms[right]:.0f} "
                f"(> {MAX_CUES_PER_SEC}/s cap)"
            )
            break  # one report is enough; avoid spamming for every overlapping window

    return result


def print_report(result: ReportResult) -> None:
    print("=== HAPTIC JSON-LOG REPORT ===")
    print(f"Total cue records: {result.total}")
    print(f"Counts by kind: {dict(result.counts_by_kind)}")
    print(f"Counts by cue: {dict(result.counts_by_cue)}")
    print(f"Min gap since previous: {result.min_gap_ms if result.min_gap_ms is not None else 'n/a'} ms")
    print(f"Max intensity (normalized 0-1): {result.max_intensity_normalized if result.max_intensity_normalized is not None else 'n/a'}")
    if result.violations:
        print(f"SAFETY CAP VIOLATIONS ({len(result.violations)}):")
        for v in result.violations:
            print(f"  - {v}")
    else:
        print("Safety cap violations: none")


def main() -> None:
    parser = argparse.ArgumentParser(description="Summarize a fake_haptic.py --json-log file")
    parser.add_argument("json_log", type=str, help="Path to the --json-log file")
    parser.add_argument("--json-out", action="store_true", help="Print the report as JSON instead of text")
    args = parser.parse_args()

    records = load_records(Path(args.json_log))
    result = analyze(records)

    if args.json_out:
        print(json.dumps(result.as_dict(), indent=2))
    else:
        print_report(result)


if __name__ == "__main__":
    main()
