# OGMR — One Game, Many ROMs

Pre-built game grouping masks for use with the `nkds ogmr` command and beyond. Each system folder contains a YAML file that maps every Redump dat entry to its canonical game, enabling per-game NKDS sets with maximum deduplication.

## General Purpose

While these OGMR files were created for NKDS, the grouping data itself is general-purpose. Any tool or workflow that needs to identify which game a ROM belongs to can use these masks — whether that's an OGMR (One Game, Many ROMs) collection keeping all variants together, a 1G1R (One Game, One ROM) scenario selecting the best single version per game, a ROM manager, a batch renaming script, or any other datfile-driven workflow. The YAML format is simple and the regex masks are standard.

## What's Here

```
OGMR/
├── dat_grouper.py                          # Script to regenerate masks from a Redump datfile
├── nintendo_gamecube/
│   ├── nintendo_gamecube_ogmr.yaml         # OGMR masks (779 games)
│   ├── translations.yaml                   # Translated game titles (foreign → English)
│   └── suggestions.yaml                    # AI-suggested mappings (accepted/rejected/pending)
├── nintendo_wii/                           # 1,774 games
├── nintendo_wiiu/                          # 228 games
├── sony_ps1/                               # 5,957 games
├── sony_ps2/                               # 6,310 games
├── sony_ps3/                               # 1,894 games
├── microsoft_xbox/                         # 1,295 games
└── microsoft_xbox_360/                     # 1,800 games
```

## Quick Start

Use the `_ogmr.yaml` file for your system directly with `nkds ogmr`:

```bash
# Import Wii images into per-game sets
nkds ogmr OGMR/nintendo_wii/nintendo_wii_ogmr.yaml --datastore D:\NKitData\Wii D:\WiiISOs --recursive --shard-size 0
```

## Current Status

| System | Dat Entries | Unique Games | Translations | Status |
|--------|:-----------:|:------------:|:------------:|:------:|
| GameCube | 2,019 | 779 | 421 | PASS |
| Wii | 3,779 | 1,774 | 1,069 | PASS |
| Wii U | 541 | 228 | 81 | PASS |
| PlayStation | 10,909 | 5,957 | 930 | PASS |
| PlayStation 2 | 11,767 | 6,310 | 639 | PASS |
| PlayStation 3 | 4,493 | 1,894 | 713 | PASS |
| Xbox | 2,678 | 1,295 | 230 | PASS |
| Xbox 360 | 3,686 | 1,800 | 315 | PASS |

All systems pass validation — zero orphans, zero false positives, each dat entry matched exactly once.

## How Masks Work

Each game entry in the YAML has a `name` (used as the NKDS set name) and a list of regex `masks`:

```yaml
games:
  - name: Example Game
    masks:
      - '^Example Game(?= \(|\.iso|\.bin|\.cue|$)'
      - '^Foreign Title(?= \(|\.iso|\.bin|\.cue|$)'
```

The boundary lookahead `(?= \(|\.iso|...)` prevents partial matches — ensuring a mask for "Example Game" doesn't accidentally match "Example Game 2".

Masks match on the base title, making them tolerant of most dat name changes (region tag edits, revision bumps, language additions). Only an actual title rename would require a mask update — and the validation step catches that.

## Regenerating Masks

If you need to update masks for a new datfile release:

```bash
python3 dat_grouper.py "Nintendo - Wii - Datfile.dat" \
    -t nintendo_wii/translations.yaml \
    -t nintendo_wii/suggestions.yaml \
    -o nintendo_wii --output-prefix grouped
```

Requires Python 3 and PyYAML (`pip install pyyaml`).

The script parses the datfile, applies title variant mappings, groups entries, generates regex masks, and validates correctness. Any unmatched entries are reported for manual review.

## Title Translation Files

The `translations.yaml` and `suggestions.yaml` files contain manually verified translations of foreign-language game titles to their canonical English name. These were time-consuming to create and verify. They can be fine-tuned where groupings are not 100% correct.

*Note: "Translations" here refers to translated game titles (e.g., a Japanese game name mapped to its English equivalent) — not language ports of the games themselves.*

## Ownership & Maintenance

This initial set of OGMR files represents a significant amount of work to produce, but I do not intend to maintain them long-term. They are being given to the community to take forward.

Ideally these lists would be maintained by or alongside the datting communities (Redump, No-Intro, Tosec) who already track game entries and have the expertise to keep groupings accurate as dats evolve. If you represent one of these communities and are interested in taking ownership, please reach out on [Discord](https://discord.gg/YT792u5yWJ).

I am also willing to align NKDS's `ogmr` command with any variant of the standard that may evolve from this initial work — whether that's a different YAML schema, a different grouping approach, or integration with existing tooling.

In the meantime, community contributions via pull requests are welcome.

## Links

- [OGMR Wiki Page](https://github.com/Nanook/NKit/wiki/OGMR)
- [NKDS Documentation](https://github.com/Nanook/NKit/wiki/NKDS)
- [Discord](https://discord.gg/YT792u5yWJ)
