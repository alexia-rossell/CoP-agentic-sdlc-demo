import assert from "node:assert/strict";
import { test } from "node:test";
import type { BacklogItem } from "../server/api-types.js";
import {
  renderBacklog, renderClarification, renderTimeline, renderValidation, validateRequestText,
} from "./render.js";

const items: BacklogItem[] = [
  { id: "DEMO-100", type: "Feature", title: "Claims <b>intake</b>", description: "d", businessValue: "v", tags: ["demo"], state: "created" },
  { id: "DEMO-101", type: "User Story", parentId: "DEMO-100", title: "Upload", persona: "Employee", goal: "g", benefit: "b",
    acceptanceCriteria: ["Criterion one"], state: "reused" },
  { id: "DEMO-102", type: "Task", parentId: "DEMO-101", title: "Scan files", description: "desc", category: "security", priority: "high", state: "created" },
];

test("request validation rejects empty and oversized text", () => {
  assert.match(validateRequestText("   ") ?? "", /Enter a software requirement/);
  assert.match(validateRequestText("x".repeat(8001)) ?? "", /under 8000/);
  assert.equal(validateRequestText("Build something"), undefined);
});

test("clarification view lists questions and states no tickets were created", () => {
  const html = renderClarification([{ topic: "users", question: "Who uses it?", reason: "Needed" }]);
  assert.match(html, /Clarification required/);
  assert.match(html, /No tickets were created during this run/);
  assert.match(html, /Who uses it\?/);
  assert.match(html, /Why: Needed/);
});

test("backlog renders a nested Feature, User Story and Task hierarchy with badges", () => {
  const html = renderBacklog(items);
  assert.ok(html.indexOf("DEMO-100") < html.indexOf("DEMO-101") && html.indexOf("DEMO-101") < html.indexOf("DEMO-102"));
  assert.match(html, /<li class="item item-user-story">[\s\S]*<ul>[\s\S]*DEMO-102/);
  assert.match(html, /Parent: DEMO-101/);
  assert.match(html, /cat-security">security/);
  assert.match(html, /high priority/);
  assert.match(html, /Created/);
  assert.match(html, /Reused/);
  assert.match(html, /Criterion one/);
  assert.match(html, /Business value/);
  assert.match(html, /Claims &lt;b&gt;intake&lt;\/b&gt;/);
  assert.ok(!html.includes("<b>intake"));
  assert.match(html, /<details class="children"><summary>1 task<\/summary>/);
  assert.match(html, /1 feature · 1 user stories · 1 tasks/);
  assert.match(renderBacklog([]), /backlog is empty/);
});

test("timeline shows phase, tool, status, ticket ID and duration", () => {
  const html = renderTimeline([{
    id: "evt-1", timestamp: "2026-01-01T00:00:00Z", phase: "tool", toolName: "create_feature",
    status: "completed", summary: "Feature: Claims", ticketId: "DEMO-100", durationMs: 12,
  }, { id: "evt-2", timestamp: "2026-01-01T00:00:01Z", phase: "review", status: "running", summary: "Review" }]);
  assert.match(html, /Create feature/);
  assert.match(html, /badge-completed">completed/);
  assert.match(html, /DEMO-100/);
  assert.match(html, /12 ms/);
  assert.match(html, /Review backlog/);
  assert.match(renderTimeline([]), /No run yet/);
});

test("validation summary shows PASS/FAIL and the non-benchmark label", () => {
  const html = renderValidation({ overall: "FAIL", checks: [
    { id: "a", label: "Check A", status: "PASS", detail: "ok" }, { id: "b", label: "Check B", status: "FAIL", detail: "bad" }] });
  assert.match(html, /Overall: <strong>FAIL/);
  assert.match(html, /result-pass">PASS/);
  assert.match(html, /result-fail">FAIL/);
  assert.match(html, /not a model-quality benchmark/);
});
