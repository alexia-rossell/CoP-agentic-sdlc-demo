import { z } from "zod";
import { type AgentResult } from "../src/agent/run-agent.js";

export const casesSchema = z.array(z.object({
  name: z.string(),
  input: z.enum(["complete", "incomplete"]),
  expectedStatus: z.enum(["needs_information", "backlog_created"]),
  requiredTools: z.array(z.string()),
  forbiddenTools: z.array(z.string()),
  requiredTaskCategories: z.array(z.string()),
}).strict()).min(1);
export type EvaluationCase = z.infer<typeof casesSchema>[number];

export function evaluateResult(testCase: EvaluationCase, result: AgentResult): string[] {
  const failures: string[] = [];
  const successful = new Set(result.calls.filter((call) => call.success).map((call) => call.name));
  if (result.status !== testCase.expectedStatus) failures.push(`Expected ${testCase.expectedStatus}`);
  for (const tool of testCase.requiredTools) {
    if (!successful.has(tool)) failures.push(`Missing successful tool: ${tool}`);
  }
  for (const tool of testCase.forbiddenTools) {
    if (result.calls.some((call) => call.name === tool)) failures.push(`Forbidden tool: ${tool}`);
  }
  if (testCase.expectedStatus === "needs_information" && (result.tickets.length || !result.questions.length)) {
    failures.push("Clarification requires questions and zero tickets");
  }
  for (const category of testCase.requiredTaskCategories) {
    if (!result.tickets.some((ticket) => ticket.type === "Task" && ticket.category === category)) {
      failures.push(`Missing task category: ${category}`);
    }
  }
  for (const ticket of result.tickets) {
    if (!result.calls.some((call) => call.success && call.id === ticket.id)) {
      failures.push(`Ticket ${ticket.id} has no successful tool result`);
    }
    if (ticket.type === "User Story" && !ticket.acceptanceCriteria.length) {
      failures.push(`Story ${ticket.id} has no acceptance criteria`);
    }
  }
  return failures;
}
