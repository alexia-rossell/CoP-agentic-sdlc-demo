import type { ApiError, BacklogItem, Example, RunState } from "../server/api-types.js";
import {
  presentationLabel, renderBacklog, renderClarification, renderError, renderRunPanels, validateRequestText, viewClasses,
} from "./render.js";

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const requestBox = $<HTMLTextAreaElement>("request");
const generate = $<HTMLButtonElement>("generate");
const reset = $<HTMLButtonElement>("reset");
const presentToggle = $<HTMLButtonElement>("present-toggle");
const inputError = $("input-error");
const runError = $("run-error");
const runStatus = $("run-status");
const timeline = $("timeline");
let examples: Example[] = [];
let busy = false;
let hasRun = false;
let presentation = false;
let seenEvents = new Set<string>();

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init, headers: init?.body ? { "content-type": "application/json" } : undefined,
  });
  const body = await response.json() as T | ApiError;
  if (!response.ok) throw new Error((body as ApiError).error ?? `Request failed (${response.status})`);
  return body as T;
}

function applyViewClasses() {
  const managed = ["is-running", "has-run", "presentation"];
  document.body.classList.remove(...managed);
  document.body.classList.add(...viewClasses({ running: busy, hasRun, presentation }));
  presentToggle.textContent = presentationLabel(presentation);
  presentToggle.setAttribute("aria-pressed", String(presentation));
}

function setBusy(value: boolean, message = "") {
  busy = value;
  for (const button of [generate, reset, ...document.querySelectorAll<HTMLButtonElement>(".example")]) button.disabled = value;
  generate.textContent = value ? "Generating…" : "Generate backlog";
  document.body.toggleAttribute("aria-busy", value);
  runStatus.textContent = message;
  applyViewClasses();
}

function showError(message: string) {
  runError.hidden = !message;
  runError.innerHTML = renderError(message);
}

async function refreshBacklog(run?: RunState) {
  const stored = await api<BacklogItem[]>("/api/backlog");
  const states = new Map((run?.result?.tickets ?? []).map((item) => [item.id, item.state]));
  const items = stored.map((item) => ({ ...item, state: states.get(item.id) ?? item.state }));
  $("backlog").innerHTML = run?.result?.status === "needs_information"
    ? renderClarification(run.result.questions)
    : renderBacklog(items);
}

function render(run: RunState | undefined) {
  const panels = renderRunPanels(run, { seen: seenEvents });
  $("phase-strip").innerHTML = panels.phaseStrip;
  const atBottom = timeline.scrollHeight - timeline.scrollTop - timeline.clientHeight < 40;
  timeline.innerHTML = panels.timeline;
  if (run?.status === "running" && atBottom) timeline.scrollTop = timeline.scrollHeight;
  $("validation").innerHTML = panels.validation;
  seenEvents = new Set((run?.events ?? []).map((event) => event.id));
  if (run?.status === "failed") showError(`Agent run failed: ${run.error ?? "unknown error"}`);
}

async function pollRun(runId: string) {
  for (;;) {
    const run = await api<RunState>(`/api/runs/${runId}`);
    render(run);
    if (run.status !== "running") return run;
    await new Promise((resolve) => setTimeout(resolve, 700));
  }
}

async function onGenerate() {
  inputError.textContent = validateRequestText(requestBox.value) ?? "";
  if (inputError.textContent) return requestBox.focus();
  showError("");
  seenEvents = new Set();
  hasRun = true;
  setBusy(true, "Running the Copilot agent workflow…");
  render({ runId: "pending", status: "running", startedAt: "", events: [] });
  try {
    const { runId } = await api<{ runId: string }>("/api/runs", {
      method: "POST", body: JSON.stringify({ request: requestBox.value, freshStart: $<HTMLInputElement>("fresh").checked }),
    });
    const run = await pollRun(runId);
    if (run.status === "completed") await refreshBacklog(run);
    else await refreshBacklog().catch(() => undefined);
    setBusy(false, run.status === "completed" ? "Run completed." : "Run failed.");
  } catch (error) {
    render(undefined);
    showError(error instanceof Error ? error.message : String(error));
    setBusy(false, "Run failed.");
  }
}

async function onReset() {
  showError("");
  setBusy(true, "Resetting…");
  try {
    await api("/api/reset", { method: "POST", body: "{}" });
    requestBox.value = "";
    inputError.textContent = "";
    hasRun = false;
    seenEvents = new Set();
    render(undefined);
    await refreshBacklog();
    setBusy(false, "Demo reset.");
  } catch (error) {
    showError(error instanceof Error ? error.message : String(error));
    setBusy(false);
  }
}

function loadExample(id: Example["id"]) {
  const example = examples.find((item) => item.id === id);
  if (!example) return;
  requestBox.value = example.request;
  inputError.textContent = "";
  requestBox.focus();
}

async function init() {
  render(undefined);
  applyViewClasses();
  generate.addEventListener("click", () => { if (!busy) void onGenerate(); });
  reset.addEventListener("click", () => { if (!busy) void onReset(); });
  presentToggle.addEventListener("click", () => { presentation = !presentation; applyViewClasses(); });
  $("load-incomplete").addEventListener("click", () => loadExample("incomplete"));
  $("load-complete").addEventListener("click", () => loadExample("complete"));
  const setOpen = (open: boolean) => {
    for (const element of document.querySelectorAll<HTMLDetailsElement>("#backlog details.wi")) element.open = open;
  };
  $("expand-all").addEventListener("click", () => setOpen(true));
  $("collapse-all").addEventListener("click", () => setOpen(false));
  try {
    examples = await api<Example[]>("/api/examples");
    await refreshBacklog();
  } catch (error) {
    showError(`Could not reach the local API: ${error instanceof Error ? error.message : String(error)}`);
    $("backlog").innerHTML = `<p class="empty">Backlog unavailable.</p>`;
  }
}

void init();
