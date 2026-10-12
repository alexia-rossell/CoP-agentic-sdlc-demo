import type { AgentRunEvent } from "../telemetry/events.js";
import type { BacklogItem, ValidationReport } from "../server/api-types.js";
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

export function renderTimeline(events: AgentRunEvent[]): string {
  if (!events.length) return `<p class="empty">No run yet. Generate a backlog to see the agentic loop.</p>`;
  return `<ol class="timeline">${events.map((event) => `
    <li class="event status-${event.status}">
      <div class="event-head">
        <span class="phase">${escapeHtml(phaseLabel(event))}</span>
        <span class="badge badge-${event.status}">${event.status}</span>
      </div>
      <p class="summary">${escapeHtml(event.summary)}</p>
      <p class="meta">${event.toolName ? `<code>${escapeHtml(event.toolName)}</code>` : ""}${
        event.ticketId ? ` <strong class="ticket-id">${escapeHtml(event.ticketId)}</strong>` : ""}${
        event.durationMs === undefined ? "" : ` <span>${event.durationMs} ms</span>`}</p>
    </li>`).join("")}</ol>`;
}

export function renderClarification(questions: Clarification[]): string {
  return `<div class="clarification" role="status">
    <h3>Clarification required</h3>
    <p>No tickets were created during this run.</p>
    <ul>${questions.map((q) => `<li><strong>${escapeHtml(q.topic)}</strong>
      <p>${escapeHtml(q.question)}</p><p class="reason">Why: ${escapeHtml(q.reason)}</p></li>`).join("")}</ul>
  </div>`;
}

const STATE_LABEL: Record<BacklogItem["state"], string> = { created: "Created", reused: "Reused", existing: "Existing" };

function renderItem(item: BacklogItem, children: string): string {
  const parent = item.type === "Feature" ? "" : `<span>Parent: ${escapeHtml(item.parentId)}</span>`;
  const task = item.type === "Task"
    ? `<span class="badge cat-${item.category}">${item.category}</span><span class="badge prio-${item.priority}">${item.priority} priority</span>`
    : "";
  let details = "";
  if (item.type === "Feature") {
    details = `<p>${escapeHtml(item.description)}</p><p><strong>Business value:</strong> ${escapeHtml(item.businessValue)}</p>
      <p><strong>Tags:</strong> ${item.tags.map(escapeHtml).join(", ")}</p>`;
  } else if (item.type === "User Story") {
    details = `<p>As a ${escapeHtml(item.persona)}, I want ${escapeHtml(item.goal)}, so that ${escapeHtml(item.benefit)}.</p>
      <p><strong>Acceptance criteria:</strong></p><ul>${item.acceptanceCriteria.map((c) => `<li>${escapeHtml(c)}</li>`).join("")}</ul>`;
  } else {
    details = `<p>${escapeHtml(item.description)}</p>`;
  }
  return `<li class="item item-${item.type.replace(" ", "-").toLowerCase()}">
    <div class="item-head"><span class="item-id">${escapeHtml(item.id)}</span>
      <span class="item-type">${item.type}</span><span class="item-title">${escapeHtml(item.title)}</span></div>
    <div class="item-meta">${parent}${task}<span class="badge state-${item.state}">${STATE_LABEL[item.state]}</span></div>
    <details><summary>Details</summary>${details}</details>${children}</li>`;
}

export function renderBacklog(items: BacklogItem[]): string {
  if (!items.length) return `<p class="empty">The demo backlog is empty.</p>`;
  const kids = (id: string, nest: (item: BacklogItem) => string, label?: string) => {
    const list = items.filter((item) => "parentId" in item && item.parentId === id);
    if (!list.length) return "";
    const tree = `<ul>${list.map(nest).join("")}</ul>`;
    return label ? `<details class="children"><summary>${list.length} ${label}${list.length === 1 ? "" : "s"}</summary>${tree}</details>` : tree;
  };
  const task = (item: BacklogItem) => renderItem(item, "");
  const story = (item: BacklogItem) => renderItem(item, kids(item.id, task, "task"));
  const features = items.filter((item) => item.type === "Feature");
  const known = new Set(items.map((item) => item.id));
  const orphans = items.filter((item) => item.type !== "Feature" && !known.has(item.parentId));
  const count = (type: string) => items.filter((item) => item.type === type).length;
  return `<p class="counts">${count("Feature")} feature · ${count("User Story")} user stories · ${count("Task")} tasks</p>
  <ul class="tree">${features.map((item) => renderItem(item, kids(item.id, story))).join("")}${
    orphans.map((item) => renderItem(item, "")).join("")}</ul>`;
}

export function renderValidation(report: ValidationReport | undefined): string {
  if (!report) return `<p class="empty">Demo validation appears after a completed run.</p>`;
  return `<div class="validation overall-${report.overall.toLowerCase()}">
    <p class="overall">Overall: <strong>${report.overall}</strong></p>
    <ul>${report.checks.map((check) => `<li><span class="badge result-${check.status.toLowerCase()}">${check.status}</span>
      <span>${escapeHtml(check.label)}</span><small>${escapeHtml(check.detail)}</small></li>`).join("")}</ul>
    <p class="note">Deterministic structural checks only; not a model-quality benchmark.</p></div>`;
}
