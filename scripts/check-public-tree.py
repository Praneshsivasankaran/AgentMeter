#!/usr/bin/env python3
"""Check versioned/candidate public files; never print secret matches."""
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]
names = subprocess.check_output(
    ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
    cwd=ROOT).decode().split("\0")
errors = []
files = sorted(set(name for name in names if name and (ROOT / name).is_file()))
for name in files:
    p = ROOT / name
    parts = {part.lower() for part in Path(name).parts}
    if parts & {"node_modules", "bin", "obj", "artifacts", "evidence", "internal", "claudebridge"}:
        errors.append(f"{name}: forbidden generated/internal directory")
    if p.suffix.lower() in {".exe", ".dll", ".pdb", ".msix", ".msixbundle", ".appx", ".zip", ".dmg", ".pfx", ".p12", ".p8", ".key", ".cer", ".log"}:
        errors.append(f"{name}: binary, credential or development output")
    if p.name.lower() in {"sdk.mjs", "auth.json", ".env"} or "claude-agent-sdk" in p.name.lower():
        errors.append(f"{name}: excluded provider or credential file")
    raw = p.read_bytes()
    if p.suffix.lower() in {".png", ".gif", ".ico"}:
        continue
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        continue
    for pattern in [
        r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
        r"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})",
        r"\bsk-ant-[A-Za-z0-9_-]{30,}",
        r"(?i)(?:[A-Z]:[\\/]+Users[\\/]+|/Users/|/home/)(?!Example User[\\/]|example[\\/]|test[\\/]|runner[\\/])[\w .-]+[\\/]",
    ]:
        if re.search(pattern, text):
            errors.append(f"{name}: possible credential or personal path")
    if p.suffix.lower() == ".md":
        links = re.findall(r"!?\[[^\]]*\]\(([^)\s]+)\)", text)
        links += re.findall(r'(?:src|href)="([^"]+)"', text)
        for link in links:
            if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", link) or link.startswith("#"):
                continue
            target = (p.parent / unquote(link.split("#")[0])).resolve()
            if not target.exists():
                errors.append(f"{name}: missing local link {link}")
if errors:
    print("\n".join(errors))
    sys.exit(1)
print(f"PASS: public file hygiene and local documentation links ({len(files)} files).")
