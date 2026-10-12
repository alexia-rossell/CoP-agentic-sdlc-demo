import type { AgentRunEvent } from "../telemetry/events.js";
import type { BacklogItem, RunState, ValidationReport } from "../server/api-types.js";
import type { Clarification } from "../tools/backlog-tools.js";

export const MAX_REQUEST_LENGTH = 8000;

export function validateRequestText(value: string): string | undefined {
  if (!value.trim()) return "Enter a software requirement before generating a backlog.";
  if (value.length > MAX_REQUEST_LENGTH) return `Keep the requirement under ${MAX_REQUEST_LENGTH} characters.`;
  return undefined;
}

export function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (char) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char] as string);
}

const plural = (count: number, one: string, many = `${one}s`) => `${count} ${count === 1 ? one : many}`;

/* ---------- page-level state ---------- */

export interface ViewFlags { running: boolean; hasRun: boolean; presentation: boolean }

export function viewClasses(flags: ViewFlags): string[] {
  return [
    ...(flags.running ? ["is-running"] : []),
    ...(flags.hasRun ? ["has-run"] : []),
    ...(flags.presentation ? ["presentation"] : []),
  ];
}

export const presentationLabel = (on: boolean): string => `Presentation mode: ${on ? "on" : "off"}`;

export function renderError(message: string): string {
  return message ? escapeHtml(message) : "";
}

/* ---------- execution: phase strip and timeline ---------- */

const TOOL_LABELS: Record<string, string> = {
  request_missing_information: "Request clarification",
  create_feature: "Create feature",
  create_user_story: "Create user story",
  create_engineering_task: "Create engineering task",
};
const PHASE_LABELS: Record<AgentRunEvent["phase"], string> = {
  assessment: "Assess requirement", clarification: "Request clarification", planning: "Plan backlog",
  tool: "Tool call", review: "Review backlog", report: "Report result",
};

export const phaseLabel = (event: AgentRunEvent): string =>
  (event.toolName && TOOL_LABELS[event.toolName]) || PHASE_LABELS[event.phase];

export type LoopStep = "understand" | "plan" | "act" | "observe" | "review";
export type StepState = "pending" | "current" | "done";

const LOOP_STEPS: { id: LoopStep; label: string }[] = [
  { id: "understand", label: "Understand" }, { id: "plan", label: "Plan" }, { id: "act", label: "Act" },
  { id: "observe", label: "Observe" }, { id: "review", label: "Review" },
];
const isCreateTool = (event: AgentRunEvent) => event.phase === "tool" && event.toolName !== "request_missing_information";
const isFinished = (event: AgentRunEvent) => event.status !== "running" && event.status !== "pending";

// Derived only from events the application already emitted: Act is a running tool call,
// Observe is a returned tool result, and Review is the review phase.
export function loopStepStates(events: AgentRunEvent[], running: boolean): Record<LoopStep, StepState> {
  const visited: Record<LoopStep, boolean> = {
    understand: events.some((e) => e.phase === "assessment" || e.phase === "clarification" || e.toolName === "request_missing_information"),
    plan: events.some((e) => e.phase === "planning"),
    act: events.some(isCreateTool),
    observe: events.some((e) => isCreateTool(e) && isFinished(e)),
    review: events.some((e) => e.phase === "review"),
  };
  let current: LoopStep | undefined;
  const last = events[events.length - 1];
  if (running && last) {
    if (visited.review) current = "review";
    else if (isCreateTool(last)) current = isFinished(last) ? "observe" : "act";
    else if (last.phase === "planning") current = "plan";
    else if (last.phase === "assessment" || last.phase === "clarification" || last.toolName === "request_missing_information") current = "understand";
  }
  const result = {} as Record<LoopStep, StepState>;
  for (const { id } of LOOP_STEPS) result[id] = id === current ? "current" : visited[id] ? "done" : "pending";
  return result;
}

const STEP_STATE_TEXT: Record<StepState, string> = { pending: "not reached", current: "in progress", done: "done" };

export function renderPhaseStrip(events: AgentRunEvent[], running: boolean): string {
  const states = loopStepStates(events, running);
  return `<ol class="phase-strip" aria-label="Agentic loop phases">${LOOP_STEPS.map(({ id, label }, index) => `
    <li class="step step-${states[id]}"${states[id] === "current" ? ' aria-current="step"' : ""}>
      <span class="step-label">${label}</span><span class="sr-only"> (${STEP_STATE_TEXT[states[id]]})</span>
    </li>${index < LOOP_STEPS.length - 1 ? '<li class="step-arrow" aria-hidden="true">→</li>' : ""}`).join("")}</ol>`;
}

const STATUS_ICON: Record<AgentRunEvent["status"], string> = {
  pending: "·", running: "◌", completed: "✓", reused: "↻", failed: "✕",
};

export function eventStateLabel(event: AgentRunEvent): string {
  if (event.status === "running") return "Running";
  if (event.status === "pending") return "Pending";
  if (event.status === "failed") return "Error";
  if (event.status === "reused") return "Reused";
  return isCreateTool(event) && event.toolName?.startsWith("create_") ? "Created" : "Done";
}

export function renderTimeline(events: AgentRunEvent[], options: { running?: boolean; seen?: ReadonlySet<string> } = {}): string {
  if (!events.length) {
    return options.running
      ? `<p class="empty loading" role="status"><span class="spinner" aria-hidden="true"></span>Starting the agent run…</p>`
      : `<p class="empty">No run yet. Tool calls appear here as the agent makes them.</p>`;
  }
  return `<ol class="timeline" aria-label="Agent execution events">${events.map((event) => `
    <li class="event status-${event.status}${options.seen && !options.seen.has(event.id) ? " enter" : ""}" data-event="${escapeHtml(event.id)}">
      <span class="event-icon" aria-hidden="true">${STATUS_ICON[event.status]}</span>
      <div class="event-body">
        <div class="event-head">
          <span class="phase">${escapeHtml(phaseLabel(event))}</span>
          ${event.toolName ? `<code class="tool">${escapeHtml(event.toolName)}</code>` : ""}
          ${event.ticketId ? `<strong class="ticket-id">${escapeHtml(event.ticketId)}</strong>` : ""}
          <span class="badge badge-${event.status}">${eventStateLabel(event)}</span>
          ${event.durationMs === undefined ? "" : `<span class="duration">${event.durationMs} ms</span>`}
        </div>
        <p class="summary">${escapeHtml(event.summary)}</p>
      </div>
    </li>`).join("")}</ol>`;
}

/* ---------- clarification ---------- */

export function renderClarification(questions: Clarification[]): string {
  return `<div class="clarification" role="status">
    <h3>Clarification required</h3>
    <p class="no-items">No backlog items created.</p>
    <ul>${questions.map((q) => `<li><span class="topic">${escapeHtml(q.topic)}</span>
      <p class="question">${escapeHtml(q.question)}</p><p class="reason"><strong>Why:</strong> ${escapeHtml(q.reason)}</p></li>`).join("")}</ul>
  </div>`;
}

/* ---------- backlog tree ---------- */

const STATE_LABEL: Record<BacklogItem["state"], string> = { created: "Created", reused: "Reused", existing: "Existing" };

function renderDetails(item: BacklogItem): string {
  if (item.type === "Feature") {
    return `<p>${escapeHtml(item.description)}</p>
      <dl><dt>Business value</dt><dd>${escapeHtml(item.businessValue)}</dd>
      <dt>Tags</dt><dd class="tags">${item.tags.map((tag) => `<span class="tag">${escapeHtml(tag)}</span>`).join("")}</dd></dl>`;
  }
  if (item.type === "User Story") {
    return `<dl><dt>Persona</dt><dd>${escapeHtml(item.persona)}</dd>
      <dt>Goal</dt><dd>${escapeHtml(item.goal)}</dd>
      <dt>Benefit</dt><dd>${escapeHtml(item.benefit)}</dd>
      <dt>Acceptance criteria</dt><dd><ul>${item.acceptanceCriteria.map((c) => `<li>${escapeHtml(c)}</li>`).join("")}</ul></dd></dl>`;
  }
  return `<p>${escapeHtml(item.description)}</p>
    <dl><dt>Category</dt><dd>${escapeHtml(item.category)}</dd><dt>Priority</dt><dd>${escapeHtml(item.priority)}</dd></dl>`;
}

function renderItem(item: BacklogItem, children: string, summaryNote = "", open = false): string {
  const task = item.type === "Task"
    ? `<span class="badge cat-${item.category}">${item.category}</span><span class="badge prio-${item.priority}">${item.priority} priority</span>`
    : "";
  return `<li class="item item-${item.type.replace(" ", "-").toLowerCase()}">
    <details class="wi"${open ? " open" : ""}>
      <summary><span class="item-id">${escapeHtml(item.id)}</span><span class="item-type">${item.type}</span>
        <span class="item-title">${escapeHtml(item.title)}</span>${summaryNote}${task}
        <span class="badge state-${item.state}">${STATE_LABEL[item.state]}</span></summary>
      <div class="wi-body">${renderDetails(item)}</div>${children}
    </details></li>`;
}

export function renderBacklog(items: BacklogItem[]): string {
  if (!items.length) return `<p class="empty">The demo backlog is empty. Generated work items appear here.</p>`;
  const kids = (id: string, nest: (item: BacklogItem) => string) => {
    const list = items.filter((item) => "parentId" in item && item.parentId === id);
    return list.length ? `<ul>${list.map(nest).join("")}</ul>` : "";
  };
  const taskCount = (id: string) => items.filter((item) => "parentId" in item && item.parentId === id).length;
  const task = (item: BacklogItem) => renderItem(item, "");
  const story = (item: BacklogItem) =>
    renderItem(item, kids(item.id, task), `<span class="child-count">${plural(taskCount(item.id), "task")}</span>`);
  const features = items.filter((item) => item.type === "Feature");
  const known = new Set(items.map((item) => item.id));
  const orphans = items.filter((item) => item.type !== "Feature" && !known.has(item.parentId));
  const count = (type: string) => items.filter((item) => item.type === type).length;
  return `<p class="counts">${plural(count("Feature"), "feature")} · ${plural(count("User Story"), "user story", "user stories")} · ${plural(count("Task"), "task")}</p>
  <ul class="tree">${features.map((item) => renderItem(item, kids(item.id, story), "", true)).join("")}${
    orphans.map((item) => renderItem(item, "")).join("")}</ul>`;
}

/* ---------- validation ---------- */

export function renderValidation(report: ValidationReport | undefined): string {
  if (!report) return `<p class="empty">Structural checks appear after a completed run.</p>`;
  return `<div class="validation overall-${report.overall.toLowerCase()}">
    <p class="overall">Overall <strong class="result result-${report.overall.toLowerCase()}">${report.overall === "PASS" ? "✓" : "✕"} ${report.overall}</strong></p>
    <ul>${report.checks.map((check) => `<li><span class="badge result-${check.status.toLowerCase()}">${check.status === "PASS" ? "✓" : "✕"} ${check.status}</span>
      <span class="check-label">${escapeHtml(check.label)}</span><small>${escapeHtml(check.detail)}</small></li>`).join("")}</ul>
    <p class="note">Deterministic structural checks only; not a model-quality benchmark.</p></div>`;
}

/* ---------- composed views ---------- */

export function renderRunPanels(run: RunState | undefined, options: { seen?: ReadonlySet<string> } = {}): {
  phaseStrip: string; timeline: string; validation: string;
} {
  const running = run?.status === "running";
  return {
    phaseStrip: renderPhaseStrip(run?.events ?? [], running),
    timeline: renderTimeline(run?.events ?? [], { running, seen: options.seen }),
    validation: renderValidation(run?.validation),
  };
}
