import type { ApiError, BacklogItem, Example, RunState } from "../server/api-types.js";
import {
  renderBacklog, renderClarification, renderTimeline, renderValidation, validateRequestText,
} from "./render.js";

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const requestBox = $<HTMLTextAreaElement>("request");
const generate = $<HTMLButtonElement>("generate");
const reset = $<HTMLButtonElement>("reset");
const inputError = $("input-error");
const runError = $("run-error");
const runStatus = $("run-status");
let examples: Example[] = [];
let busy = false;

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init, headers: init?.body ? { "content-type": "application/json" } : undefined,
  });
  const body = await response.json() as T | ApiError;
  if (!response.ok) throw new Error((body as ApiError).error ?? `Request failed (${response.status})`);
  return body as T;
}

function setBusy(value: boolean, message = "") {
  busy = value;
  for (const button of [generate, reset, ...document.querySelectorAll<HTMLButtonElement>(".example")]) button.disabled = value;
  generate.textContent = value ? "Generating…" : "Generate backlog";
  document.body.toggleAttribute("aria-busy", value);
  runStatus.textContent = message;
}

function showError(message: string) {
  runError.hidden = !message;
  runError.textContent = message;
}

async function refreshBacklog(run?: RunState) {
  const stored = await api<BacklogItem[]>("/api/backlog");
  const states = new Map((run?.result?.tickets ?? []).map((item) => [item.id, item.state]));
  const items = stored.map((item) => ({ ...item, state: states.get(item.id) ?? item.state }));
  $("backlog").innerHTML = run?.result?.status === "needs_information"
    ? renderClarification(run.result.questions)
    : renderBacklog(items);
}

function render(run: RunState) {
  $("timeline").innerHTML = renderTimeline(run.events);
  $("validation").innerHTML = renderValidation(run.validation);
  if (run.status === "failed") showError(`Agent run failed: ${run.error ?? "unknown error"}`);
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
  setBusy(true, "Running the Copilot agent workflow…");
  try {
    const { runId } = await api<{ runId: string }>("/api/runs", {
      method: "POST", body: JSON.stringify({ request: requestBox.value, freshStart: $<HTMLInputElement>("fresh").checked }),
    });
    const run = await pollRun(runId);
    if (run.status === "completed") await refreshBacklog(run);
    else await refreshBacklog().catch(() => undefined);
    setBusy(false, run.status === "completed" ? "Run completed." : "Run failed.");
  } catch (error) {
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
    $("timeline").innerHTML = renderTimeline([]);
    $("validation").innerHTML = renderValidation(undefined);
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
  $("timeline").innerHTML = renderTimeline([]);
  $("validation").innerHTML = renderValidation(undefined);
  generate.addEventListener("click", () => { if (!busy) void onGenerate(); });
  reset.addEventListener("click", () => { if (!busy) void onReset(); });
  $("load-incomplete").addEventListener("click", () => loadExample("incomplete"));
  $("load-complete").addEventListener("click", () => loadExample("complete"));
  const setOpen = (open: boolean) => {
    for (const element of document.querySelectorAll<HTMLDetailsElement>("#backlog details.children")) element.open = open;
  };
  $("expand-all").addEventListener("click", () => setOpen(true));
  $("collapse-all").addEventListener("click", () => setOpen(false));
  try {
    examples = await api<Example[]>("/api/examples");
    await refreshBacklog();
  } catch (error) {
    showError(`Could not reach the local API: ${error instanceof Error ? error.message : String(error)}`);
  }
}

void init();
