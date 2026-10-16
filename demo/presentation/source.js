async function loadSource() {
  const file = new URLSearchParams(location.search).get("file");
  const allowed = file && !file.split("/").some(part => part === ".." || part === "") &&
    /^(?:(?:app|agent|tests|demo|specs)\/|\.github\/skills\/|\.specify\/memory\/|(?:AGENTS|README|DEMO_AGENDA)\.md$)/.test(file) &&
    /^[a-zA-Z0-9_./-]+$/.test(file);
  if (!allowed) throw new Error("Choose a repository source link from the slide deck.");
  const url = new URL(`../../${file}`, location.href);
  const response = await fetch(url);
  if (!response.ok) throw new Error(`Cannot load ${file}: HTTP ${response.status}`);
  const source = await response.text();
  document.title = `${file} / demo source`;
  document.getElementById("name").textContent = file;
  const raw = document.getElementById("raw");
  raw.href = url.href;
  raw.hidden = false;
  const fragment = document.createDocumentFragment();
  for (const [index, text] of source.split("\n").entries()) {
    const line = document.createElement("span");
    line.id = `line-${index + 1}`;
    line.className = "line";
    const number = document.createElement("a");
    number.className = "number";
    number.href = `#${line.id}`;
    number.textContent = String(index + 1);
    number.setAttribute("aria-label", `Line ${index + 1}`);
    line.append(number, document.createTextNode(text));
    fragment.append(line);
  }
  document.getElementById("content").append(fragment);
  const target = /^#line-\d+$/.test(location.hash) ? document.getElementById(location.hash.slice(1)) : null;
  target?.scrollIntoView({ block: "center" });
}

loadSource().catch(error => {
  const message = document.getElementById("error");
  message.textContent = error.message;
  message.hidden = false;
});
