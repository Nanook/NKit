#!/usr/bin/env python3
"""
dat_grouper.py — Generic Redump datfile grouper.

Takes any Redump XML datfile (.xml or .dat), groups games by canonical
English title, generates regex masks, and produces a review report of
likely foreign titles that need translation.

Handles both single-ROM-per-game (GameCube/Wii/Wii U) and multi-ROM-per-game
(PS1/PS2/Saturn) formats by grouping on <game name="..."> rather than
individual <rom name="..."> entries.

Usage:
    python3 dat_grouper.py <datfile> [--translations translations.yaml]

Outputs (using datfile basename as prefix):
    <prefix>_grouped.yaml          — games with ROMs
    <prefix>_grouped_masks.yaml    — games with masks + ROMs
    <prefix>_grouped.md            — Markdown format
    <prefix>_review.txt            — foreign titles needing translation

The --translations flag loads a YAML file of manual overrides:
    translations:
      "Biohazard 4": "Resident Evil 4"
      "Zelda no Densetsu - Twilight Princess": "The Legend of Zelda: Twilight Princess"
"""
import argparse
import os
import re
import sys
import xml.etree.ElementTree as ET


# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------
parser = argparse.ArgumentParser(description='Group Redump datfile by game title')
parser.add_argument('datfile', help='Path to Redump XML datfile (.xml or .dat)')
parser.add_argument('--translations', '-t', action='append', default=[],
                    help='YAML file with manual title translations (can be repeated)')
parser.add_argument('--no-auto-merge', action='store_true',
                    help='Disable automatic merging of platform suffix variants')
parser.add_argument('--output-dir', '-o', default=None,
                    help='Directory to write output files (default: current directory)')
parser.add_argument('--output-prefix', default=None,
                    help='Override output filename prefix (default: derived from datfile name)')
args = parser.parse_args()

dat_path = args.datfile
prefix = re.sub(r'\.(xml|dat)$', '', os.path.basename(dat_path), flags=re.IGNORECASE)

# Output directory
output_dir = args.output_dir if args.output_dir else '.'
if output_dir != '.' and not os.path.isdir(output_dir):
    os.makedirs(output_dir, exist_ok=True)

# Simplify prefix for output filenames
if args.output_prefix:
    # When --output-prefix is given, use it as the full base name
    # e.g. --output-prefix grouped -> grouped.yaml, grouped_masks.yaml, grouped.md
    prefix_short = args.output_prefix
else:
    prefix_short = re.sub(r'\s*-\s*Datfile.*$', '', prefix).strip().lower().replace(' ', '_').replace('-', '_').replace('__', '_')

print(f"Datfile: {dat_path}")
print(f"Output prefix: {prefix_short}")
print(f"Output dir: {output_dir}")


# ---------------------------------------------------------------------------
# Load translations if provided
# ---------------------------------------------------------------------------
TRANSLATE = {}
if args.translations:
    try:
        import yaml
        for tfile in args.translations:
            with open(tfile, 'r', encoding='utf-8') as f:
                data = yaml.safe_load(f)
                entries = data.get('translations', {})
                TRANSLATE.update(entries)
            print(f"Loaded {len(entries)} translations from {tfile}")
        print(f"Total translations: {len(TRANSLATE)}")
    except ImportError:
        print("WARNING: PyYAML not installed, cannot load translations file")
    except Exception as e:
        print(f"WARNING: Could not load translations: {e}")


# ---------------------------------------------------------------------------
# Parse the datfile
# ---------------------------------------------------------------------------
print(f"Parsing {dat_path}...")
tree = ET.parse(dat_path)
root = tree.getroot()

# Extract header info
header = root.find('header')
platform_name = header.find('name').text if header is not None and header.find('name') is not None else 'Unknown'
print(f"Platform: {platform_name}")

# Extract all games with their ROMs
# Key insight: group on <game name="..."> not on individual <rom name="...">
# For single-ROM games (GC/Wii), game name ≈ rom name minus .iso
# For multi-ROM games (PS2), game name is the zip/folder name
games_raw = []
for game_el in root.findall('game'):
    game_name = game_el.get('name', '')
    # Decode XML entities (ET does this automatically for attributes)
    roms = []
    for rom_el in game_el.findall('rom'):
        rom_name = rom_el.get('name', '')
        roms.append(rom_name)
    games_raw.append({
        'game_name': game_name,
        'roms': roms,
    })

print(f"Total entries in dat: {len(games_raw)}")
total_roms = sum(len(g['roms']) for g in games_raw)
print(f"Total ROM files: {total_roms}")


# ---------------------------------------------------------------------------
# Normalization helpers
# ---------------------------------------------------------------------------
def strip_meta(name):
    """Remove all parenthesized metadata from a game/ROM name."""
    # Handle nested parentheses by repeatedly stripping innermost groups
    n = name
    prev = None
    while prev != n:
        prev = n
        n = re.sub(r'\s*\([^()]*\)', '', n)
    # Also strip .iso/.bin/.cue etc
    n = re.sub(r'\.(iso|bin|cue|img|ccd|sub|mds|mdf|wav|ape|flac|ogg)$', '', n, flags=re.IGNORECASE)
    return n.strip()


def normalize(name):
    """Convert a raw game name to a canonical English group title."""
    base = strip_meta(name)

    # Direct translation lookup
    if base in TRANSLATE:
        return TRANSLATE[base]

    # Handle "Title, The - Subtitle" -> "The Title: Subtitle"
    m = re.match(r'^(.+?),\s*(The|Le|La|Les|Der|Die|Das|El|Il|I|Los|Las|Gli|Lo)\s*-\s*(.+)$', base)
    if m:
        article = m.group(2)
        if article not in ('The',):
            article = 'The'
        return f"{article} {m.group(1)}: {m.group(3)}"

    # Handle "Title, The" without subtitle
    m = re.match(r'^(.+?),\s*(The|Le|La|Les|Der|Die|Das|El|Il|Los|Las|Gli|Lo)$', base)
    if m:
        article = m.group(2)
        if article not in ('The',):
            article = 'The'
        return f"{article} {m.group(1)}"

    # Replace " - " with ": " for subtitles
    base = re.sub(r'\s+-\s+', ': ', base)
    return base


# ---------------------------------------------------------------------------
# Detect region from parenthesized metadata
# ---------------------------------------------------------------------------
ENGLISH_REGIONS = {'USA', 'Europe', 'UK', 'Australia', 'Canada', 'World'}
JAPANESE_REGION = {'Japan'}
GERMAN_REGION = {'Germany'}
FRENCH_REGION = {'France'}
SPANISH_REGION = {'Spain'}
ITALIAN_REGION = {'Italy'}
KOREAN_REGION = {'Korea'}
DUTCH_REGION = {'Netherlands'}
SWEDISH_REGION = {'Sweden'}
PORTUGUESE_REGION = {'Portugal', 'Brazil'}

def get_regions(name):
    """Extract region names from parenthesized metadata."""
    regions = set()
    for m in re.finditer(r'\(([^)]+)\)', name):
        content = m.group(1)
        # First parenthesized group is usually the region
        for part in content.split(','):
            part = part.strip()
            if part in ENGLISH_REGIONS | JAPANESE_REGION | GERMAN_REGION | FRENCH_REGION | \
                       SPANISH_REGION | ITALIAN_REGION | KOREAN_REGION | DUTCH_REGION | \
                       SWEDISH_REGION | PORTUGUESE_REGION | {'Latin America', 'Asia', 'China', 'Taiwan'}:
                regions.add(part)
    return regions


def is_english_region(regions):
    """Check if any region is English-speaking."""
    return bool(regions & ENGLISH_REGIONS)


def is_foreign_only(regions):
    """Check if all regions are non-English."""
    return regions and not is_english_region(regions)


# ---------------------------------------------------------------------------
# Group games
# ---------------------------------------------------------------------------
groups = {}
for g in games_raw:
    english = normalize(g['game_name'])
    if english not in groups:
        groups[english] = {'game_names': [], 'roms': []}
    groups[english]['game_names'].append(g['game_name'])
    groups[english]['roms'].extend(g['roms'])

# Sort
sorted_groups = dict(
    sorted(groups.items(), key=lambda x: x[0].lower().lstrip('0123456789 '))
)

# ---------------------------------------------------------------------------
# Auto-merge: combine groups that differ only by platform/edition suffixes
# ---------------------------------------------------------------------------
PLATFORM_SUFFIXES = [
    ' Wii', ' Wii U', ' PS2', ' PS3', ' PS4', ' HD', ' HD Remaster',
    ' HD Version', ' HD Edition', ' HD Ver.', ' Remastered', ' Remaster',
    ' Special Edition', ' Complete Edition', ' Game of the Year Edition',
    ' Definitive Edition', ' Ultimate Edition', ' Gold Edition',
    ' Platinum Edition', ' Greatest Hits', ' Essentials',
    ' for Wii', ' for Wii U', ' for PlayStation 2',
]

if not args.no_auto_merge:
    merge_count = 0
    titles_to_remove = []
    for suffix in PLATFORM_SUFFIXES:
        for title in list(sorted_groups.keys()):
            if title.endswith(suffix):
                base = title[:-len(suffix)]
                if base in sorted_groups and title != base:
                    # Merge into the base group
                    sorted_groups[base]['game_names'].extend(sorted_groups[title]['game_names'])
                    sorted_groups[base]['roms'].extend(sorted_groups[title]['roms'])
                    titles_to_remove.append(title)
                    merge_count += 1
    for t in set(titles_to_remove):
        if t in sorted_groups:
            del sorted_groups[t]
    if merge_count > 0:
        print(f"Auto-merged {merge_count} platform/edition suffix variants")
        # Re-sort after merges
        sorted_groups = dict(
            sorted(sorted_groups.items(), key=lambda x: x[0].lower().lstrip('0123456789 '))
        )

print(f"Unique games (grouped): {len(sorted_groups)}")


# ---------------------------------------------------------------------------
# Generate masks (from game names, not ROM filenames)
# ---------------------------------------------------------------------------
def name_to_mask(game_name):
    """Derive a regex mask from a game name.
    Strips metadata parens (region, language, rev, etc.) but keeps
    title-embedded parens, then creates an anchored regex."""
    METADATA_KEYWORDS = {
        'USA', 'Europe', 'Japan', 'Korea', 'France', 'Germany', 'Spain',
        'Italy', 'UK', 'Australia', 'Canada', 'World', 'Asia', 'China',
        'Taiwan', 'Latin America', 'Netherlands', 'Sweden', 'Brazil',
        'Portugal', 'Rev', 'Disc', 'Beta', 'Demo', 'Proto', 'Unl',
        'Sample', 'Taikenban', 'Track', 'Multi Tap',
        # PS1/PS2-specific metadata patterns
        'SLED', 'SCES', 'SCUS', 'SCPS', 'SLPS', 'SLUS', 'SLES', 'SLPM',
        'Major Wave', 'Trade Demo', 'Bonus', 'Green Disc', 'Red Disc',
        'Blue Disc', 'White Disc', 'Fukyuuban', 'Present-ban',
        'Honpen', 'Taisen Game', 'Strategy', 'Cheat',
        'Activision', 'GT Interactive', 'Virgin Interactive',
        'Nescafe', 'Final Fantasy', 'Episode', 'Langrisser',
        'Shockwave', 'Mahjong de', 'Hanafuda de', 'PlayStation',
        'History of', 'Super Robot', 'Minadzuki', 'Akai', 'Shirayuki',
        '2-dai Hero', 'Nescafe-ban',
        # Date patterns like (2001-02-19)
        '200', '199', '198',
    }
    n = game_name
    # Remove parenthesized groups that start with a metadata keyword
    # Keep groups that are part of the title (e.g., "(Future)", "(Ryaku)")
    def is_metadata(paren_content):
        first_word = paren_content.split(',')[0].split(')')[0].strip()
        # Check if first word is a metadata keyword
        for kw in METADATA_KEYWORDS:
            if first_word.startswith(kw):
                return True
        # Language codes like "En,Fr,De"
        if re.match(r'^[A-Z][a-z](?:,[A-Z][a-z])*\)?$', first_word):
            return True
        return False

    # Process from right to left, stripping metadata parens
    result = n
    for m in reversed(list(re.finditer(r'\s*\(([^()]*)\)', n))):
        if is_metadata(m.group(1)):
            result = result[:m.start()] + result[m.end():]

    # Also handle nested parens: strip outer metadata that contains inner parens
    # e.g., "(Multi Tap (SCPH-10090) Doukonban)" 
    for m in reversed(list(re.finditer(r'\s*\(([^)]*)\)', n))):
        content = m.group(1)
        if is_metadata(content.split('(')[0].strip()):
            result = result[:m.start()] + result[m.end():]

    # Strip file extensions
    result = re.sub(r'\.(iso|bin|cue|img|ccd|sub|mds|mdf|wav|ape|flac|ogg)$', '', result, flags=re.IGNORECASE)
    result = result.strip()

    # Escape regex metacharacters (not spaces)
    result = re.sub(r'([.+?^${}()|[\]\\\\*])', r'\\\1', result)
    # Lookahead boundary
    return f"^{result}(?= \\(|\\.iso|\\.bin|\\.cue|$)"

# ---------------------------------------------------------------------------
# YAML escape
# ---------------------------------------------------------------------------
def yaml_escape(s):
    """Single-quote values only when YAML requires it."""
    needs_quoting = (
        ': ' in s
        or s.endswith(':')
        or any(c in s for c in '{}[]&*?|>!%@`#\\')
        or s.startswith('- ')
        or s.startswith("'")
    )
    if needs_quoting:
        escaped = s.replace("'", "''")
        return f"'{escaped}'"
    return s


# ---------------------------------------------------------------------------
# Write outputs
# ---------------------------------------------------------------------------
total_children = 0

# --- Markdown ---
md_lines = [
    f"# {platform_name} - Grouped Game List",
    "",
    f"Source: {os.path.basename(dat_path)}",
    f"Total entries in dat: {len(games_raw)}",
    f"Total ROM files: {total_roms}",
    f"Unique games (grouped): {len(sorted_groups)}",
    "", "---", "",
]
for title, data in sorted_groups.items():
    total_children += len(data['game_names'])
    md_lines.append(f"## {title}")
    for gn in sorted(data['game_names']):
        md_lines.append(f"  - {gn}")
    md_lines.append("")
md_lines.extend(["---", f"Total game entries grouped: {total_children}"])

md_path = os.path.join(output_dir, f"{prefix_short}.md")
with open(md_path, 'w', encoding='utf-8') as f:
    f.write('\n'.join(md_lines))
print(f"Wrote {md_path}")

# --- YAML ---
yaml_lines = [
    f"# {platform_name} - Grouped Game List",
    f"# Source: {os.path.basename(dat_path)}",
    f"# Total entries: {len(games_raw)}",
    f"# Unique games: {len(sorted_groups)}",
    "", "games:",
]
for title, data in sorted_groups.items():
    yaml_lines.append(f"  - name: {yaml_escape(title)}")
    yaml_lines.append(f"    entries:")
    for gn in sorted(data['game_names']):
        yaml_lines.append(f"      - {yaml_escape(gn)}")

yaml_path = os.path.join(output_dir, f"{prefix_short}.yaml")
with open(yaml_path, 'w', encoding='utf-8') as f:
    f.write('\n'.join(yaml_lines) + '\n')
print(f"Wrote {yaml_path}")

# --- YAML with masks ---

# Priority order for picking the representative filename from entries
REGION_PRIORITY = ['USA', 'Europe', 'World', 'UK', 'Australia', 'Canada', 'Asia']

def pick_rom_name(game_names):
    """Pick the best entry name to use as the filename-safe game name.
    Prefers USA, then Europe, then World, etc. Uses strip_meta to get
    the base name (with ' - ' separators, no parentheses)."""
    best = None
    best_score = len(REGION_PRIORITY) + 1  # lower is better
    for gn in game_names:
        regions = get_regions(gn)
        for i, r in enumerate(REGION_PRIORITY):
            if r in regions:
                if i < best_score:
                    best_score = i
                    best = gn
                break
    # Fallback: first English-region entry, or just the first entry
    if best is None:
        for gn in sorted(game_names):
            regions = get_regions(gn)
            if regions & set(REGION_PRIORITY):
                best = gn
                break
        if best is None:
            best = sorted(game_names)[0]
    return strip_meta(best)

mask_lines = [
    f"# {platform_name} - Grouped Game List with Masks",
    f"# Source: {os.path.basename(dat_path)}",
    f"# Total entries: {len(games_raw)}",
    f"# Unique games: {len(sorted_groups)}",
    "#",
    "# filename: safe filename derived from the USA/English ROM entry",
    "# masks: regex patterns to match game entries from the dat",
    "# entries: original <game name> values from the dat",
    "# roms: individual ROM files within each game entry",
    "", "games:",
]
gmr_entries = []  # collect (rom_name, masks) for 1gmr output
for title, data in sorted_groups.items():
    # Derive unique masks from game names
    seen_masks = []
    for gn in sorted(data['game_names']):
        mask = name_to_mask(gn)
        if mask not in seen_masks:
            seen_masks.append(mask)

    # Fallback: if any entry isn't matched by the generated masks,
    # add an exact-match mask for it (handles edge cases like
    # embedded region text in game names)
    for gn in data['game_names']:
        matched = False
        for mask in seen_masks:
            if re.search(mask, gn):
                matched = True
                break
        if not matched:
            escaped = re.sub(r'([.+?^${}()|[\]\\*])', r'\\\1', gn)
            exact = f"^{escaped}$"
            if exact not in seen_masks:
                seen_masks.append(exact)

    rom_name = pick_rom_name(data['game_names'])
    mask_lines.append(f"  - name: {yaml_escape(title)}")
    mask_lines.append(f"    filename: {yaml_escape(rom_name)}")
    mask_lines.append(f"    masks:")
    for m in seen_masks:
        mask_lines.append(f"      - {yaml_escape(m)}")
    mask_lines.append(f"    entries:")
    for gn in sorted(data['game_names']):
        mask_lines.append(f"      - {yaml_escape(gn)}")
    if data['roms']:
        mask_lines.append(f"    roms:")
        for r in sorted(set(data['roms'])):
            mask_lines.append(f"      - {yaml_escape(r)}")

    # Collect for 1gmr output
    gmr_entries.append((rom_name, seen_masks))

masks_path = os.path.join(output_dir, f"{prefix_short}_masks.yaml")
with open(masks_path, 'w', encoding='utf-8') as f:
    f.write('\n'.join(mask_lines) + '\n')
print(f"Wrote {masks_path}")

# --- 1GMR output (filename + masks only, extracted from grouped_masks) ---
system_name = os.path.basename(os.path.abspath(output_dir)) if output_dir != '.' else prefix_short
gmr_lines = [
    f"# {platform_name} - 1GMR Game Masks",
    f"# Source: {os.path.basename(dat_path)}",
    f"# Unique games: {len(sorted_groups)}",
    "", "games:",
]
for rom_name, masks in gmr_entries:
    gmr_lines.append(f"  - name: {yaml_escape(rom_name)}")
    gmr_lines.append(f"    masks:")
    for m in masks:
        gmr_lines.append(f"      - {yaml_escape(m)}")

gmr_path = os.path.join(output_dir, f"{system_name}_1gmr.yaml")
with open(gmr_path, 'w', encoding='utf-8') as f:
    f.write('\n'.join(gmr_lines) + '\n')
print(f"Wrote {gmr_path}")


# ---------------------------------------------------------------------------
# Generate review report — find likely foreign titles needing translation
# ---------------------------------------------------------------------------
review_lines = [
    f"# {platform_name} - Translation Review Report",
    f"# Source: {os.path.basename(dat_path)}",
    f"# Groups: {len(sorted_groups)}",
    "",
    "# This report lists groups that likely need manual translation.",
    "# Priority 1: Foreign-only groups (no English region ROMs)",
    "# Priority 2: Groups with names that look non-English",
    "# Priority 3: Potential merge candidates (similar names)",
    "",
]

# Detect foreign-only groups
foreign_only = []
for title, data in sorted_groups.items():
    all_regions = set()
    for gn in data['game_names']:
        all_regions |= get_regions(gn)
    if is_foreign_only(all_regions):
        foreign_only.append((title, all_regions, len(data['game_names'])))

review_lines.append(f"=" * 70)
review_lines.append(f"PRIORITY 1: Foreign-only groups ({len(foreign_only)} found)")
review_lines.append(f"These groups have NO English-region ROMs.")
review_lines.append(f"=" * 70)
review_lines.append("")

# Sort by region then name
for title, regions, count in sorted(foreign_only, key=lambda x: (sorted(x[1])[0] if x[1] else '', x[0])):
    region_str = ', '.join(sorted(regions)) if regions else 'unknown'
    review_lines.append(f"  [{region_str}] {title} ({count} entries)")

# Suggest matches for foreign titles using word overlap
english_titles = [t for t in sorted_groups.keys()
                  if not any(t == ft for ft, _, _ in foreign_only)]
if foreign_only and english_titles:
    review_lines.append("")
    review_lines.append(f"=" * 70)
    review_lines.append(f"SUGGESTED MATCHES (word overlap)")
    review_lines.append(f"Foreign titles with possible English equivalents.")
    review_lines.append(f"=" * 70)
    review_lines.append("")

    def word_set(s):
        """Extract significant words (3+ chars) from a title."""
        return set(w.lower() for w in re.findall(r'[A-Za-z0-9]{3,}', s))

    english_word_index = {}
    for et in english_titles:
        for w in word_set(et):
            english_word_index.setdefault(w, []).append(et)

    suggestions = []
    for title, regions, count in foreign_only:
        fw = word_set(title)
        if not fw:
            continue
        # Score each English title by word overlap
        scores = {}
        for w in fw:
            for et in english_word_index.get(w, []):
                scores[et] = scores.get(et, 0) + 1
        if scores:
            best = max(scores.items(), key=lambda x: x[1])
            ew = word_set(best[0])
            overlap = fw & ew
            # Only suggest if overlap is significant (2+ words or 50%+ of foreign words)
            if best[1] >= 2 or (best[1] >= 1 and len(fw) <= 2):
                pct = best[1] / max(len(fw), 1) * 100
                suggestions.append((title, best[0], best[1], pct))

    for ftitle, etitle, overlap, pct in sorted(suggestions, key=lambda x: -x[2]):
        review_lines.append(f"  {ftitle}")
        review_lines.append(f"    -> {etitle} ({overlap} words, {pct:.0f}%)")
        review_lines.append("")

# Detect potential merge candidates — groups whose base names are similar
review_lines.append("")
review_lines.append(f"=" * 70)
review_lines.append(f"PRIORITY 2: Potential merge candidates")
review_lines.append(f"Groups where one name is a prefix/variant of another.")
review_lines.append(f"=" * 70)
review_lines.append("")

all_titles = sorted(sorted_groups.keys())
merge_candidates = []
for i, t1 in enumerate(all_titles):
    for t2 in all_titles[i+1:]:
        # Check if t1 is a prefix of t2 (with word boundary)
        if t2.startswith(t1) and len(t2) > len(t1):
            suffix = t2[len(t1):]
            # Only flag if the suffix starts with a space, colon, or number
            if suffix[0] in ' :0123456789':
                merge_candidates.append((t1, t2))
        # Check for "X" vs "X Wii" / "X PS2" etc
        for platform_suffix in [' Wii', ' PS2', ' PS3', ' HD', ' Remastered', ' Special Edition']:
            if t2 == t1 + platform_suffix:
                merge_candidates.append((t1, t2))

# Deduplicate and limit
seen = set()
for t1, t2 in merge_candidates[:200]:
    key = (t1, t2)
    if key not in seen:
        seen.add(key)
        review_lines.append(f"  {t1}")
        review_lines.append(f"    vs  {t2}")
        review_lines.append("")

# Summary stats
review_lines.append("")
review_lines.append(f"=" * 70)
review_lines.append(f"SUMMARY")
review_lines.append(f"=" * 70)
review_lines.append(f"Total groups: {len(sorted_groups)}")
review_lines.append(f"Foreign-only groups: {len(foreign_only)}")
review_lines.append(f"Potential merge candidates: {len(seen)}")
review_lines.append(f"")
review_lines.append(f"To fix: create a translations.yaml file with entries like:")
review_lines.append(f"  translations:")
review_lines.append(f'    "Foreign Title": "English Title"')
review_lines.append(f"Then re-run: python3 dat_grouper.py {os.path.basename(dat_path)} -t translations.yaml")

review_path = os.path.join(output_dir, "review.txt") if args.output_prefix else \
    os.path.join(output_dir, f"{prefix_short}_review.txt")
with open(review_path, 'w', encoding='utf-8') as f:
    f.write('\n'.join(review_lines))
print(f"Wrote {review_path}")


# ---------------------------------------------------------------------------
# Validate masks
# ---------------------------------------------------------------------------
print()
print("Validating masks...")

all_game_names = [g['game_name'] for g in games_raw]
game_name_to_group = {}
for title, data in sorted_groups.items():
    for gn in data['game_names']:
        game_name_to_group[gn] = title

errors = 0
claimed = {}
for title, data in sorted_groups.items():
    expected = set(data['game_names'])
    masks = []
    for gn in sorted(data['game_names']):
        m = name_to_mask(gn)
        if m not in masks:
            masks.append(m)

    # Fallback: add exact-match masks for entries not matched
    for gn in data['game_names']:
        matched_by_existing = False
        for mask in masks:
            try:
                if re.search(mask, gn):
                    matched_by_existing = True
                    break
            except re.error:
                pass
        if not matched_by_existing:
            escaped = re.sub(r'([.+?^${}()|[\]\\*])', r'\\\1', gn)
            exact = f"^{escaped}$"
            if exact not in masks:
                masks.append(exact)

    matched = set()
    for mask in masks:
        try:
            pattern = re.compile(mask)
        except re.error:
            errors += 1
            continue
        for gn in all_game_names:
            if pattern.search(gn):
                matched.add(gn)

    fp = matched - expected
    if fp:
        errors += 1
        for gn in sorted(fp)[:3]:
            print(f"  FALSE POSITIVE [{title}]: matched '{gn}' (belongs to '{game_name_to_group.get(gn, '?')}')")

    missed = expected - matched
    if missed:
        errors += 1
        for gn in sorted(missed)[:3]:
            print(f"  MISSED [{title}]: '{gn}'")

    for gn in matched:
        if gn in claimed:
            errors += 1
        else:
            claimed[gn] = title

orphans = set(all_game_names) - set(claimed.keys())

print(f"  Games: {len(sorted_groups)}, Entries: {len(all_game_names)}, "
      f"Claimed: {len(claimed)}, Orphans: {len(orphans)}, Errors: {errors}")

if errors == 0 and not orphans:
    print("  PASS — all masks valid")
else:
    print(f"  ISSUES FOUND — {errors} errors, {len(orphans)} orphans")
    if orphans:
        for o in sorted(orphans)[:5]:
            print(f"    orphan: {o}")

print()
print(f"Done. Review {review_path} for translation candidates.")
