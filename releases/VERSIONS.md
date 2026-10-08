# Tested Versions

Only builds that were **actually tested** go here. Each row points to a git tag that contains all edits up to that point,
so checking out the tag reproduces the tested build.

| Date | Component | Version / tag | Commit | Test evidence (log) | Artifact | Known issues |
|---|---|---|---|---|---|---|
| 2026-09-14 | analytics + sim + contracts | `analytics-v0.1.0` | see tag | pytest 28 passed; validate.py exit 0; 13 fixture sessions valid (logs/sessions/2026-09-14-N-*.md, *-metrics-conform.md) | n/a (Python) | peak speed 6–11 % error on severe/noisy; nSUB + SPARC unreliable under tracking dropout |

Artifacts (APKs, web builds) go in `releases/<component>/<version>/` (gitignored) and are attached to a GitHub Release with the same tag.
