import type { AgentResult } from "../agent/run-agent.js";
import type { ValidationCheck, ValidationReport } from "./api-types.js";

const REQUIRED_CATEGORIES = ["security", "testing", "observability", "operations"] as const;
const CREATE_TOOLS = ["create_feature", "create_user_story", "create_engineering_task"];

// Structural demo checks only; this is not a model-quality benchmark.
export function validateRun(result: Pick<AgentResult, "status" | "questions" | "tickets" | "calls">): ValidationReport {
  const { status, questions, tickets, calls } = result;
  const successful = calls.filter((call) => call.success);
  const created = successful.filter((call) => CREATE_TOOLS.includes(call.name));
  const check = (id: string, label: string, pass: boolean, detail: string): ValidationCheck =>
    ({ id, label, status: pass ? "PASS" : "FAIL", detail });
  const checks: ValidationCheck[] = [];

  if (status === "needs_information") {
    const complete = questions.length > 0 && questions.every((q) => q.topic && q.question && q.reason);
    checks.push(check("clarification", "Correct clarification behaviour", complete && !created.length,
      complete ? "Structured questions returned before any ticket creation" : "No complete clarification questions"));
    checks.push(check("no-tickets", "No tickets created during this run", !tickets.length && !created.length,
      `${created.length} successful creation call(s), ${tickets.length} ticket(s) returned`));
  } else {
    const ids = tickets.map((ticket) => ticket.id);
    const byId = new Map(tickets.map((ticket) => [ticket.id, ticket]));
    const calledIds = new Set(created.flatMap((call) => call.id ? [call.id] : []));
    const ofType = (type: string) => tickets.filter((ticket) => ticket.type === type);
    const covered = (type: string, tool: string) =>
      ofType(type).length > 0 && ofType(type).every((ticket) => successful.some((c) => c.name === tool && c.id === ticket.id));
    const missing = REQUIRED_CATEGORIES.filter((category) =>
      !tickets.some((ticket) => ticket.type === "Task" && ticket.category === category));
    const badParents = tickets.filter((ticket) => {
      if (ticket.type === "Feature") return false;
      return byId.get(ticket.parentId)?.type !== (ticket.type === "User Story" ? "Feature" : "User Story");
    });
    const emptyStories = ofType("User Story").filter((story) =>
      !tickets.some((ticket) => ticket.type === "Task" && ticket.parentId === story.id));
    const untraced = ids.filter((id) => !calledIds.has(id));

    checks.push(check("clarification", "Correct clarification behaviour", !questions.length &&
      !calls.some((call) => call.name === "request_missing_information"),
      "Complete request proceeded without clarification questions"));
    checks.push(check("feature", "Feature created or reused", covered("Feature", "create_feature"),
      `${ofType("Feature").length} Feature(s) from successful tool results`));
    checks.push(check("story", "User Story created or reused", covered("User Story", "create_user_story"),
      `${ofType("User Story").length} User Story item(s) from successful tool results`));
    checks.push(check("categories", "Required task-category coverage", !missing.length,
      missing.length ? `Missing: ${missing.join(", ")}` : `Covered: ${REQUIRED_CATEGORIES.join(", ")}`));
    checks.push(check("hierarchy", "Valid parent-child hierarchy", !badParents.length && !emptyStories.length,
      badParents.length || emptyStories.length ? "Invalid parent or story without tasks" : "Feature → User Story → Task"));
    checks.push(check("unique-ids", "No duplicate IDs", new Set(ids).size === ids.length, `${ids.length} ticket ID(s)`));
    checks.push(check("traceable", "No ticket IDs absent from successful tool results", !untraced.length,
      untraced.length ? `Untraced: ${untraced.join(", ")}` : "Every ticket ID came from a successful tool call"));
  }
  return { overall: checks.every((item) => item.status === "PASS") ? "PASS" : "FAIL", checks };
}
