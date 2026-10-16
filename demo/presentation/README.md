# Reveal.js fallback deck

Six slides: compensation rules; Spec Kit skills/artifacts; custom controller
commands; SDK tool execution; actual verification; publishing/running the agent.
Each stage states which component performs it. Detailed narration is in speaker notes.

**Edit [slides.md](slides.md)** to change the deck. `---` separates slides;
`Note:` starts speaker notes. Markdown tables, links and fenced code are rendered
by Reveal.js's local Markdown plugin. `index.html` is only the loader/theme/logo.

## Open

From the repository root:

```bash
npm ci --prefix demo/presentation
npm start --prefix demo/presentation
```

Open <http://127.0.0.1:4180/demo/presentation/>.
Only Reveal.js is installed; there is no CDN, external font or cloud dependency.
The slides use Reveal.js's standard white theme with its bundled local fonts.
The Microsoft corner mark is a local [SVG from Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Microsoft_logo.svg);
Microsoft's trademark rights remain with Microsoft.
After installation, the presentation and its captured evidence work offline.
Serve over localhost rather than opening a `file://` URL so source links work.
The server binds to localhost and serves the repository: do not expose it publicly.

**Controls:** arrows / Space advance; Escape shows all slides; S opens presenter
notes. Add `?print-pdf` before the URL fragment and use the browser's Print -> Save
as PDF if needed. Code and captured-output links open separate tabs.

## What was actually captured

- Run `fallback-20261016`, explicit down rounding, local presenter approvals.
- Real before/after compilation and 12 acceptance checks.
- Offline rehearsal with deterministic tool selection: **not real AI inference**.
- A separate **real Copilot SDK/CLI runtime integration test** against a scripted
  loopback provider. That proves tool wiring, not real-model quality.
- No ADO publication, Foundry hosting or gateway/cloud trace is claimed.
- `dotnet publish` and a full rehearsal through the published agent DLL.

Selected verbatim output and focused source excerpts are embedded in the slides.
The complete logs, reports and code snapshots are in [captured/](captured/);
links work without the ignored `.demo/` workspace.
Hashes and approval labels are local demo evidence, not a tamper-proof audit.
The captured run retains its actual ID. Use a fresh run ID for another walkthrough.

## Recapture

From the repository root after changing demo code:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet restore Demo.slnx --locked-mode
bash demo/presentation/capture.sh a-new-run-id
```

The script builds, runs the six CLI steps in separate processes, exports evidence
and runs the local SDK integration test. It also publishes the agent and runs a
second rehearsal with a `-pkg` suffix through the published DLL. It never resets an existing run. It
updates the captured files, not the static excerpts in the slides. Review those
excerpts and dates after recapturing, then run:

```bash
python3 demo/presentation/check.py
```

No real-model credentials are needed. Generated output is intentionally tracked
for fallback use; review it for sensitive information before committing. The
capture contains only synthetic claims, local approval labels and local paths.
