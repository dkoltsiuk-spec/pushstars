"""Check English source copy; Russian translations belong in the localization catalog.

Run with Python 3: python tools/check_english.py
Generated OTA copies are rebuilt from the authored scenes by OtaSetup before iOS builds.
This check preserves internal GameObject names and the font's supported character alphabet.
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CYRILLIC = re.compile(r'[\u0400-\u04ff]')
CS_TOKEN = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"')
YAML_QUOTED = re.compile(r'"(?:\\.|[^"\\])*"')


def check():
    failures = []
    source_count = asset_count = 0
    for folder in ('Scripts', 'Editor'):
        for path in sorted((ROOT / 'Assets/_Project' / folder).rglob('*.cs')):
            source_count += 1
            source = path.read_text(encoding='utf-8-sig')
            for match in CS_TOKEN.finditer(source):
                token = match.group()
                if token.startswith(('//', '/*')) or not CYRILLIC.search(token):
                    continue
                if path.name == 'FontSetup.cs' and re.fullmatch('"[\u0400-\u04ff]+"', token):
                    continue
                if path.name == 'Localization.cs' and token == '"Русский"':
                    continue  # Native language name stays recognizable in the language picker.
                failures.append((str(path.relative_to(ROOT)), source.count('\n', 0, match.start()) + 1, token))
    paths = list((ROOT / 'Assets/_Project').rglob('*')) + [ROOT / 'Assets/testCV.unity']
    for path in sorted(paths):
        if path.suffix not in ('.unity', '.prefab', '.asset') or 'Fonts' in path.parts or 'OTA' in path.parts:
            continue
        asset_count += 1
        source = path.read_text(encoding='utf-8-sig')
        for match in YAML_QUOTED.finditer(source):
            raw = match.group()
            if not (CYRILLIC.search(raw) or re.search(r'\\u04[0-9a-fA-F]{2}', raw)):
                continue
            prefix = source[source.rfind('\n', 0, match.start()) + 1:match.start()].strip()
            if prefix == 'm_Name:':
                continue
            folded = re.sub(r'\\\r?\n[ \t]*', '', raw)
            folded = re.sub(r'[ \t]*\r?\n[ \t]*', ' ', folded)
            folded = re.sub(r'\\x([0-9a-fA-F]{2})', r'\\u00\1', folded)
            try:
                value = json.loads(folded)
            except ValueError:
                value = raw  # Unrecognized escaping must not silently skip untranslated UI.
            if CYRILLIC.search(value) or re.search(r'\\u04[0-9a-fA-F]{2}', value):
                failures.append((str(path.relative_to(ROOT)), source.count('\n', 0, match.start()) + 1, value))
        for match in re.finditer(r'^\s*m_text: ([^"\r\n][^\r\n]*)', source, re.M):
            if CYRILLIC.search(match.group(1)):
                failures.append((str(path.relative_to(ROOT)), source.count('\n', 0, match.start()) + 1, match.group(1)))
    report = {'source_files': source_count, 'asset_files': asset_count, 'failures': failures}
    output = ROOT / 'output/mobile-audit'
    output.mkdir(parents=True, exist_ok=True)
    (output / 'english-copy.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'English audit: {source_count} C# files, {asset_count} authored assets, {len(failures)} untranslated strings.')
    for path, line, value in failures:
        print(f'{path}:{line}: {value!r}')
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(check())
