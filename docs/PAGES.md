# AgentMeter public pages

GitHub Pages hosts the personal AgentMeter landing, privacy and support pages.
The developer/publisher is Pranesh S. The repository remains the source and
release home; the site does not claim either Store is available.

## Public URLs

Use these exact, case-sensitive URLs for product metadata:

| Field | URL |
| --- | --- |
| Website | https://praneshsivasankaran.github.io/AgentMeter/ |
| Privacy Policy | https://praneshsivasankaran.github.io/AgentMeter/privacy/ |
| Support | https://praneshsivasankaran.github.io/AgentMeter/support/ |

All three pages are public and require no account. GitHub Issues remains the
support destination; creating an issue requires a GitHub account.

## Maintenance

Edit the small templates and stylesheet in `site/`. The three-bar mark follows
the original AgentMeter identity in `macos/AgentMeter/Views/Brand.swift`.
There are no runtime scripts, external assets, dependencies, analytics or forms.

`PRIVACY.md` is the substantive privacy source of truth. The build renders its
current paragraphs and inline code directly, then tests exact visible-text
parity. If richer Markdown is introduced, extend the renderer and its tests;
unsupported structures fail the build rather than silently dropping content.

From the repository root:

```sh
python scripts/build-site.py
python scripts/test-site.py
python scripts/check-public-tree.py
python -m http.server 4173 --bind 127.0.0.1 --directory _site
```

Preview the landing page, privacy and support routes locally. Check desktop and
narrow layouts. Generated output stays in ignored `_site/`; only those static
files are uploaded.

## Deployment

The **AgentMeter Pages** workflow uses GitHub's configure, upload and deploy
Pages actions. Repository Pages settings use **GitHub Actions** as the source,
with HTTPS enforced and no custom domain. The workflow obtains the canonical
base URL from GitHub's Pages configuration. It uses the built-in workflow token
and deployment identity; no repository secrets are required.

Site or privacy changes on main deploy automatically. Pull requests build and
validate without deploying. The workflow can also be dispatched manually.
For metadata-only commits marked `[skip ci]` to avoid rebuilding applications,
dispatch `pages.yml` on main explicitly after pushing.

After deployment, verify the workflow's exact published URL and all three routes
using anonymous HTTPS requests and a logged-out/private browser. Preserve the
case of the project path and the trailing slash. Store metadata should use the
rendered pages, not GitHub raw or blob policy links.
