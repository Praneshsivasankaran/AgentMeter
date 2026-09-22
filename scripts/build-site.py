#!/usr/bin/env python3
"""Build static Pages output with no dependencies; PRIVACY.md stays authoritative."""
import argparse
import html
from pathlib import Path
import re
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1]


def render_privacy(source):
    heading, separator, body = source.strip().partition("\n")
    if heading != "# Privacy" or not separator:
        raise ValueError("Expected PRIVACY.md's # Privacy heading")
    paragraphs = []
    for block in re.split(r"\n\s*\n", body.strip()):
        # The current policy uses paragraphs and inline code only. Stop instead of
        # silently dropping meaning when richer Markdown is introduced.
        if re.search(r"(?m)^\s*(?:#|[-*+] |\d+\. |>)|!?\[.*\]\(|\*\*|__", block) or chr(96) * 3 in block:
            raise ValueError("Policy formatting changed; extend rendering and parity tests")
        if block.count(chr(96)) % 2:
            raise ValueError("Unbalanced inline code in privacy policy")
        safe = html.escape(block)
        safe = re.sub(chr(96) + "([^" + chr(96) + "]+)" + chr(96), r"<code>\1</code>", safe)
        paragraphs.append(f"      <p>{safe}</p>")
    return "\n".join(paragraphs)


def build(output, base_url=""):
    if base_url:
        parsed = urlsplit(base_url)
        if parsed.scheme != "https" or not parsed.netloc or parsed.query or parsed.fragment:
            raise ValueError("The canonical Pages base URL must be HTTPS without query/fragment")
        base_url = base_url.rstrip("/") + "/"
    output.mkdir(parents=True, exist_ok=True)
    allowed = {"index.html", "privacy/index.html", "support/index.html", "styles.css", "mark.svg", ".nojekyll"}
    existing = {p.relative_to(output).as_posix() for p in output.rglob("*") if p.is_file()}
    if existing - allowed or any(p.is_symlink() for p in output.rglob("*")):
        raise ValueError("Unexpected files or links in site output; use a fresh output directory")
    layout = (ROOT / "site/layout.html").read_text(encoding="utf-8")
    policy = render_privacy((ROOT / "PRIVACY.md").read_text(encoding="utf-8"))
    privacy = (
        '    <h1>AgentMeter Privacy Policy</h1>\n'
        '    <p class="lead">Developer/Publisher: <strong>Pranesh S</strong></p>\n'
        '    <div class="policy">\n' + policy + '\n    </div>\n'
        '    <p><a href="../">Back to AgentMeter</a> · '
        '<a href="https://github.com/Praneshsivasankaran/AgentMeter">GitHub repository</a></p>'
    )
    pages = [
        ("", "AgentMeter", "AgentMeter: a native desktop utility for monitoring Codex and Claude Code usage on macOS and Windows.", (ROOT / "site/index.html").read_text(encoding="utf-8")),
        ("privacy/", "AgentMeter Privacy Policy", "AgentMeter privacy policy. Developer/Publisher: Pranesh S.", privacy),
        ("support/", "AgentMeter Support", "Help with AgentMeter bugs, installation and provider compatibility.", (ROOT / "site/support.html").read_text(encoding="utf-8")),
    ]
    for route, title, description, content in pages:
        values = {"TITLE": html.escape(title), "DESCRIPTION": html.escape(description, quote=True),
                  "PREFIX": "../" if route else "./", "CONTENT": content,
                  "CANONICAL": f'<link rel="canonical" href="{html.escape(base_url + route, quote=True)}">' if base_url else ""}
        rendered = layout
        for key, value in values.items():
            rendered = rendered.replace("{{" + key + "}}", value)
        if "{{" in rendered:
            raise ValueError("Unresolved page template token")
        target = output / route / "index.html"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(rendered, encoding="utf-8")
    for name in ("styles.css", "mark.svg"):
        (output / name).write_bytes((ROOT / "site" / name).read_bytes())
    (output / ".nojekyll").write_text("", encoding="utf-8")
    print("Built three static pages; privacy text comes directly from PRIVACY.md.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=ROOT / "_site")
    parser.add_argument("--base-url", default="")
    args = parser.parse_args()
    build(args.output.resolve(), args.base_url)
