import type { Clarification } from "../tools/backlog-tools.js";
import type { Ticket } from "../storage/tickets.js";
import type { AgentRunEvent } from "../telemetry/events.js";

export type ItemState = "created" | "reused" | "existing";
export type BacklogItem = Ticket & { state: ItemState };

export interface Example { id: "incomplete" | "complete"; label: string; request: string }

export interface ValidationCheck { id: string; label: string; status: "PASS" | "FAIL"; detail: string }
export interface ValidationReport { overall: "PASS" | "FAIL"; checks: ValidationCheck[] }

export interface RunResult {
  status: "needs_information" | "backlog_created";
  questions: Clarification[];
  tickets: BacklogItem[];
  summary: string;
}

export interface RunState {
  runId: string;
  status: "running" | "completed" | "failed";
  startedAt: string;
  finishedAt?: string;
  events: AgentRunEvent[];
  result?: RunResult;
  validation?: ValidationReport;
  error?: string;
}

export interface ApiError { error: string }
