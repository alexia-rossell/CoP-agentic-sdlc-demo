"""Check fallback slide evidence and local links without third-party dependencies."""

import html.parser
import json
import re
from pathlib import Path
from urllib.parse import unquote, urlsplit, parse_qs


class Slides(html.parser.HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []
        self.excerpts = []
        self.ids = set()
        self.excerpt = None
        self.sections = 0

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if "id" in attrs:
            assert attrs["id"] not in self.ids, f"Duplicate ID: {attrs['id']}"
            self.ids.add(attrs["id"])
        if tag == "section" and "data-markdown" not in attrs:
            self.sections += 1
        if tag in ("a", "link", "script", "img"):
            url = attrs.get("href") or attrs.get("src")
            if url:
                self.links.append(url)
        if tag == "code" and "data-source" in attrs:
            self.excerpt = [attrs["data-source"], ""]

    def handle_data(self, data):
        if self.excerpt is not None:
            self.excerpt[1] += data

    def handle_endtag(self, tag):
        if tag == "code" and self.excerpt is not None:
            self.excerpts.append(self.excerpt)
            self.excerpt = None


deck = Path(__file__).resolve().parent
repo = deck.parent.parent
slides = Slides()
slides.feed((deck / "index.html").read_text())
markdown = (deck / "slides.md").read_text()
slides.sections += len(re.split(r"^---$", markdown, flags=re.MULTILINE))
slides.ids.update(re.findall(r'<!-- \.slide: id="([^"]+)" -->', markdown))
slides.links.extend(re.findall(r"\[[^\]]+\]\(([^)]+)\)", markdown))
slides.excerpts.extend(re.findall(
    r"<!-- excerpt: ([^\n]+) -->\n```[^\n]*\n(.*?)\n```", markdown, flags=re.DOTALL
))
for link in slides.links:
    url = urlsplit(link)
    if url.scheme:
        continue
    if not url.path:
        assert url.fragment.removeprefix("/") in slides.ids, link
        continue
    target = deck / unquote(url.path)
    assert target.exists(), f"Missing linked file: {link}"
    if url.path == "source.html":
        file = parse_qs(url.query)["file"][0]
        source = repo / file
        assert source.is_file(), f"Missing source: {file}"
        if url.fragment:
            line = int(url.fragment.removeprefix("line-"))
            assert 1 <= line <= len(source.read_text().splitlines()), link

for source, excerpt in slides.excerpts:
    original = (deck / source).read_text()
    lines = [line.strip() for line in original.splitlines()]
    for line in excerpt.splitlines():
        assert not line.strip() or any(line.strip() in candidate for candidate in lines), (
            f"Excerpt does not match {source}: {line}"
        )

captured = deck / "captured"
baseline = json.loads((captured / "baseline-results.json").read_text())
candidate = json.loads((captured / "candidate-results.json").read_text())
status = json.loads((captured / "05-status.json").read_text())
assert not baseline["passed"] and sum(not item["passed"] for item in baseline["results"]) == 2
assert candidate["passed"] and len(candidate["results"]) == 12
assert all(item["passed"] for item in candidate["results"])
assert status["stage"] == "AwaitPublicationApproval" and status["publicationApproval"] is None
assert status["mode"] == "rehearsal"
assert "Test Run Successful." in (captured / "sdk-runtime-test.log").read_text()
assert "SDD / Copilot SDK demo (.NET only)" in (captured / "published-help.log").read_text()
assert "[AwaitPublicationApproval] verify: PASS:" in (captured / "packaged-run.log").read_text()
assert slides.sections == 9
print(f"PASS: {slides.sections} slides, {len(slides.excerpts)} excerpts, local links and captured results.")
