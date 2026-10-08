"""Event-based trial segmentation: group events.ndjson rows by `trial` index and pull out
the timestamps of the key event types each metric needs.
"""
from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class Trial:
    index: int
    block: int
    hand: str | None
    outcome: str | None
    target: dict | None
    t_trial_start: float | None = None
    t_target_shown: float | None = None
    t_movement_onset: float | None = None
    t_contact: float | None = None
    t_grasp: float | None = None
    t_release: float | None = None
    t_placed: float | None = None
    t_trial_end: float | None = None
    events: list = field(default_factory=list)

    @property
    def reaction_time_ms(self) -> float | None:
        if self.t_movement_onset is None or self.t_target_shown is None:
            return None
        return self.t_movement_onset - self.t_target_shown

    @property
    def movement_time_ms(self) -> float | None:
        if self.t_contact is None or self.t_movement_onset is None:
            return None
        return self.t_contact - self.t_movement_onset

    @property
    def attempted(self) -> bool:
        """False for pure timeouts where the patient never moved."""
        return self.t_movement_onset is not None


def segment_trials(events: list[dict]) -> list[Trial]:
    by_trial: dict[int, list[dict]] = {}
    for ev in events:
        t = ev.get("trial")
        if t is None:
            continue
        by_trial.setdefault(t, []).append(ev)

    trials = []
    for idx in sorted(by_trial):
        evs = sorted(by_trial[idx], key=lambda e: e["t_ms"])
        tr = Trial(index=idx, block=evs[0].get("block", 0), hand=None, outcome=None, target=None, events=evs)
        for ev in evs:
            typ = ev["type"]
            if typ == "trial_start":
                tr.t_trial_start = ev["t_ms"]
            elif typ == "target_shown":
                tr.t_target_shown = ev["t_ms"]
                tr.target = ev.get("target")
            elif typ == "movement_onset":
                tr.t_movement_onset = ev["t_ms"]
                tr.hand = ev.get("hand") or tr.hand
            elif typ == "contact":
                tr.t_contact = ev["t_ms"]
                tr.hand = ev.get("hand") or tr.hand
            elif typ == "grasp":
                tr.t_grasp = ev["t_ms"]
            elif typ == "release":
                tr.t_release = ev["t_ms"]
            elif typ == "placed":
                tr.t_placed = ev["t_ms"]
            elif typ == "trial_end":
                tr.t_trial_end = ev["t_ms"]
                tr.outcome = ev.get("outcome")
        trials.append(tr)
    return trials
