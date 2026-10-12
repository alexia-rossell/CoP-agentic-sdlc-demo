import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import type { BacklogItem, RunState, ValidationReport } from "../server/api-types.js";
import type { AgentRunEvent } from "../telemetry/events.js";
import {
  loopStepStates, presentationLabel, renderBacklog, renderClarification, renderError, renderPhaseStrip,
  renderRunPanels, renderTimeline, renderValidation, validateRequestText, viewClasses,
} from "./render.js";

const items: BacklogItem[] = [
  { id: "DEMO-100", type: "Feature", title: "Claims <b>intake</b>", description: "feature desc", businessValue: "Saves time", tags: ["demo", "claims"], state: "created" },
  { id: "DEMO-101", type: "User Story", parentId: "DEMO-100", title: "Upload", persona: "Employee", goal: "upload a file", benefit: "faster intake",
    acceptanceCriteria: ["Criterion one"], state: "reused" },
  { id: "DEMO-102", type: "Task", parentId: "DEMO-101", title: "Scan files", description: "task desc", category: "security", priority: "high", state: "created" },
];

const evt = (id: string, patch: Partial<AgentRunEvent>): AgentRunEvent => ({
  id, timestamp: "2026-01-01T00:00:00Z", phase: "tool", status: "completed", summary: "s", ...patch,
});
const assess = evt("e1", { phase: "assessment", summary: "Requirement is sufficiently specified", durationMs: 40 });
const plan = evt("e2", { phase: "planning", summary: "Planning" });
const created = evt("e3", { toolName: "create_feature", summary: "Feature: Claims", ticketId: "DEMO-100", durationMs: 12 });
const reusedStory = evt("e4", { toolName: "create_user_story", status: "reused", ticketId: "DEMO-101" });
const failedTask = evt("e5", { toolName: "create_engineering_task", status: "failed", summary: "Invalid parent" });
const review = evt("e6", { phase: "review", status: "running", summary: "Review" });

const report = (overall: "PASS" | "FAIL"): ValidationReport => ({ overall, checks: [
  { id: "a", label: "Check A", status: "PASS", detail: "ok" },
  { id: "b", label: "Check B", status: overall === "PASS" ? "PASS" : "FAIL", detail: "bad" }] });

test("request validation rejects empty and oversized text", () => {
  assert.match(validateRequestText("   ") ?? "", /Enter a software requirement/);
  assert.match(validateRequestText("x".repeat(8001)) ?? "", /under 8000/);
  assert.equal(validateRequestText("Build something"), undefined);
});

test("initial empty state explains each region without showing errors", () => {
  const panels = renderRunPanels(undefined);
  assert.match(panels.timeline, /No run yet/);
  assert.match(panels.validation, /appear after a completed run/);
  assert.match(renderBacklog([]), /backlog is empty/);
  assert.ok(!/error/i.test(renderBacklog([])));
  assert.deepEqual(Object.values(loopStepStates([], false)), ["pending", "pending", "pending", "pending", "pending"]);
  assert.deepEqual(viewClasses({ running: false, hasRun: false, presentation: false }), []);
});

test("loading state shows a status message and a current phase from real events only", () => {
  assert.match(renderTimeline([], { running: true }), /role="status"[^>]*>[\s\S]*Starting the agent run/);
  const start: RunState = { runId: "r", status: "running", startedAt: "", events: [evt("e1", { phase: "assessment", status: "running" })] };
  const html = renderRunPanels(start).phaseStrip;
  assert.match(html, /step-current" aria-current="step">\s*<span class="step-label">Understand/);
  assert.match(html, /Plan<\/span><span class="sr-only"> \(not reached\)/);
  assert.deepEqual(viewClasses({ running: true, hasRun: true, presentation: false }), ["is-running", "has-run"]);
});

test("phase strip follows understand, plan, act, observe and review", () => {
  const states = (events: AgentRunEvent[], running = true) => loopStepStates(events, running);
  assert.equal(states([assess, plan]).understand, "done");
  assert.equal(states([assess, plan]).plan, "current");
  assert.equal(states([assess, plan, evt("t", { toolName: "create_feature", status: "running" })]).act, "current");
  const observed = states([assess, plan, created]);
  assert.equal(observed.observe, "current");
  assert.equal(observed.act, "done");
  assert.equal(states([assess, plan, created, review]).review, "current");
  const finished = states([assess, plan, created, { ...review, status: "completed" }], false);
  assert.deepEqual(Object.values(finished), ["done", "done", "done", "done", "done"]);
  assert.match(renderPhaseStrip([assess], false), /UNDERSTAND|Understand/);
  assert.match(renderPhaseStrip([assess], false), /aria-label="Agentic loop phases"/);
});

test("incomplete requirement stops after understand and shows no act, observe or review", () => {
  const clarify = evt("c", { phase: "tool", toolName: "request_missing_information", summary: "4 question(s)" });
  const states = loopStepStates([assess, clarify, evt("r", { phase: "report" })], false);
  assert.equal(states.understand, "done");
  assert.deepEqual([states.plan, states.act, states.observe, states.review], ["pending", "pending", "pending", "pending"]);
});

test("clarification panel lists topic, question and reason and says no items were created", () => {
  const html = renderClarification([{ topic: "users", question: "Who uses it?", reason: "Needed" }]);
  assert.match(html, /Clarification required/);
  assert.match(html, /No backlog items created\./);
  assert.match(html, /class="topic">users/);
  assert.match(html, /Who uses it\?/);
  assert.match(html, /<strong>Why:<\/strong> Needed/);
  assert.ok(!/class="error/.test(html));
});

test("successful run renders a feature, story and task hierarchy with counts", () => {
  const html = renderBacklog(items);
  assert.ok(html.indexOf("DEMO-100") < html.indexOf("DEMO-101") && html.indexOf("DEMO-101") < html.indexOf("DEMO-102"));
  assert.match(html, /<li class="item item-user-story">[\s\S]*<ul>[\s\S]*DEMO-102/);
  assert.match(html, /1 feature · 1 user story · 1 task/);
  assert.match(html, /class="child-count">1 task</);
  assert.match(html, /cat-security">security/);
  assert.match(html, /high priority/);
  assert.match(html, /Claims &lt;b&gt;intake&lt;\/b&gt;/);
  assert.ok(!html.includes("<b>intake"));
});

test("work items are expandable with type specific details", () => {
  const html = renderBacklog(items);
  assert.equal((html.match(/<details class="wi"/g) ?? []).length, 3);
  assert.match(html, /<details class="wi" open>/);
  assert.match(html, /<dt>Business value<\/dt><dd>Saves time<\/dd>/);
  assert.match(html, /class="tag">claims/);
  assert.match(html, /<dt>Persona<\/dt><dd>Employee<\/dd>[\s\S]*<dt>Goal<\/dt><dd>upload a file<\/dd>[\s\S]*<dt>Benefit<\/dt><dd>faster intake/);
  assert.match(html, /<dt>Acceptance criteria<\/dt><dd><ul><li>Criterion one/);
  assert.match(html, /task desc[\s\S]*<dt>Category<\/dt><dd>security<\/dd><dt>Priority<\/dt><dd>high/);
});

test("created and reused state are shown as text badges", () => {
  const html = renderBacklog(items);
  assert.match(html, /state-created">Created/);
  assert.match(html, /state-reused">Reused/);
  const timeline = renderTimeline([created, reusedStory, failedTask]);
  assert.match(timeline, /badge-completed">Created/);
  assert.match(timeline, /badge-reused">Reused/);
  assert.match(timeline, /badge-failed">Error/);
});

test("category badges cover every category", () => {
  const html = renderBacklog([...items.slice(0, 2), ...(["implementation", "security", "testing", "observability", "operations"] as const)
    .map((category, index): BacklogItem => ({ id: `DEMO-2${index}`, type: "Task", parentId: "DEMO-101", title: category, description: "d",
      category, priority: "low", state: "created" }))]);
  for (const category of ["implementation", "security", "testing", "observability", "operations"]) {
    assert.match(html, new RegExp(`cat-${category}">${category}`));
  }
  const css = readFileSync(fileURLToPath(new URL("../../../src/web/styles.css", import.meta.url)), "utf8");
  for (const category of ["implementation", "security", "testing", "observability", "operations"]) assert.match(css, new RegExp(`\\.cat-${category}`));
});

test("timeline shows icon, phase, monospace tool name, ticket ID, duration and state", () => {
  const html = renderTimeline([created, review]);
  assert.match(html, /class="event-icon" aria-hidden="true">✓/);
  assert.match(html, /Create feature/);
  assert.match(html, /<code class="tool">create_feature<\/code>/);
  assert.match(html, /<strong class="ticket-id">DEMO-100<\/strong>/);
  assert.match(html, /class="duration">12 ms/);
  assert.match(html, /Review backlog/);
  assert.match(html, /badge-running">Running/);
  assert.ok(!/class="duration"[^>]*>[^<]*<\/span>\s*<\/div>\s*<p class="summary">Review/.test(html), "no duration is invented for a running event");
  assert.match(renderTimeline([]), /No run yet/);
});

test("only unseen events are flagged for the entry transition", () => {
  const html = renderTimeline([assess, created], { seen: new Set(["e1"]) });
  assert.match(html, /event status-completed" data-event="e1"/);
  assert.match(html, /event status-completed enter" data-event="e3"/);
});

test("timeline escapes event text", () => {
  assert.ok(!renderTimeline([evt("x", { summary: "<img src=x>" })]).includes("<img"));
});

test("validation shows overall and each check as PASS or FAIL with text labels", () => {
  const pass = renderValidation(report("PASS"));
  assert.match(pass, /Overall <strong class="result result-pass">✓ PASS/);
  assert.match(pass, /result-pass">✓ PASS<\/span>\s*<span class="check-label">Check A/);
  assert.match(pass, /not a model-quality benchmark/);
  const fail = renderValidation(report("FAIL"));
  assert.match(fail, /Overall <strong class="result result-fail">✕ FAIL/);
  assert.match(fail, /result-fail">✕ FAIL<\/span>\s*<span class="check-label">Check B/);
  assert.ok(!/Foundry|quality score/i.test(pass.replace("not a model-quality benchmark", "")));
});

test("API errors are escaped for display and empty messages clear the panel", () => {
  assert.equal(renderError("<script>x</script>"), "&lt;script&gt;x&lt;/script&gt;");
  assert.equal(renderError(""), "");
});

test("presentation mode toggles classes and label and is styled in the stylesheet", () => {
  assert.deepEqual(viewClasses({ running: false, hasRun: true, presentation: true }), ["has-run", "presentation"]);
  assert.equal(presentationLabel(true), "Presentation mode: on");
  assert.equal(presentationLabel(false), "Presentation mode: off");
  const css = readFileSync(fileURLToPath(new URL("../../../src/web/styles.css", import.meta.url)), "utf8");
  assert.match(css, /body\.presentation \{ font-size/);
  assert.match(css, /\.presentation \.examples/);
  assert.match(css, /prefers-reduced-motion: reduce/);
  assert.match(css, /:focus-visible/);
});

test("page markup has the required landmarks, controls and labels", () => {
  const html = readFileSync(fileURLToPath(new URL("../../../src/web/index.html", import.meta.url)), "utf8");
  for (const text of ["Agentic SDLC", "From software requirement to engineering backlog", "GitHub Copilot SDK",
    "Demo environment · Synthetic data", "What should we build?", "Incomplete example", "Complete example", "Generate backlog",
    "Reset demo", "Engineering backlog", "Demo validation", "Agentic loop"]) assert.ok(html.includes(text), text);
  assert.match(html, /id="present-toggle"[^>]*aria-pressed="false"/);
  assert.match(html, /<label for="request"/);
  assert.match(html, /aria-live="polite"/);
  assert.ok(!/gradient/.test(readFileSync(fileURLToPath(new URL("../../../src/web/styles.css", import.meta.url)), "utf8")));
});
