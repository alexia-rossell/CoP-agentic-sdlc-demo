import { defineTool, type Tool } from "@github/copilot-sdk";
import { z } from "zod";
import {
  featureInput, storyInput, taskInput,
  type NewTicket, type Ticket, type TicketStore,
} from "../storage/tickets.js";
import { shorten, type RunRecorder } from "../telemetry/events.js";

export const clarificationInput = z.object({
  questions: z.array(z.object({
    topic: z.string().trim().min(1),
    question: z.string().trim().min(1),
    reason: z.string().trim().min(1),
  }).strict()).min(1),
}).strict();
export type Clarification = z.infer<typeof clarificationInput>["questions"][number];
export interface ToolCall {
  name: string;
  success: boolean;
  id?: string;
  created?: boolean;
}

const failure = (error: unknown) => shorten(`Rejected: ${error instanceof Error ? error.message : String(error)}`);

export function createBacklogTools(store: TicketStore, recorder?: RunRecorder) {
  const calls: ToolCall[] = [];
  const questions: Clarification[] = [];
  const tickets = new Map<string, Ticket>();
  let writesEnabled = false;

  async function create(name: string, input: NewTicket) {
    const eventId = recorder?.begin({ phase: "tool", toolName: name, summary: `${input.type}: ${shorten(String(input.title), 70)}` });
    try {
      if (!writesEnabled || questions.length) throw new Error("Ticket creation requires a complete assessment");
      if (input.type !== "Feature") {
        const expected = input.type === "User Story" ? "Feature" : "User Story";
        if (tickets.get(input.parentId)?.type !== expected) {
          throw new Error(`Resolve the ${expected} parent through its creation tool before creating child tickets`);
        }
      }
      const result = await store.create(input);
      tickets.set(result.item.id, result.item);
      calls.push({ name, success: true, id: result.item.id, created: result.created });
      if (eventId) recorder?.finish(eventId, { status: result.created ? "completed" : "reused", ticketId: result.item.id });
      return { ...result.item, created: result.created };
    } catch (error) {
      calls.push({ name, success: false });
      if (eventId) recorder?.finish(eventId, { status: "failed", summary: failure(error) });
      throw error;
    }
  }

  const clarification = defineTool("request_missing_information", {
    description: "Return essential clarification questions. Do not create any tickets for an incomplete request.",
    parameters: clarificationInput.toJSONSchema(),
    skipPermission: true,
    handler: (args) => {
      const eventId = recorder?.begin({
        phase: "clarification", toolName: "request_missing_information", summary: "Request clarification",
      });
      try {
        const parsed = clarificationInput.parse(args);
        if (writesEnabled) throw new Error("Clarification must happen before ticket creation");
        questions.push(...parsed.questions);
        calls.push({ name: "request_missing_information", success: true });
        if (eventId) {
          recorder?.finish(eventId, {
            status: "completed",
            summary: `${parsed.questions.length} question(s): ${parsed.questions.map((q) => q.topic).join(", ")}`,
          });
        }
        return { status: "needs_information", questions: parsed.questions };
      } catch (error) {
        if (eventId) recorder?.finish(eventId, { status: "failed", summary: failure(error) });
        throw error;
      }
    },
  });
  const creation: Tool[] = [
    defineTool("create_feature", {
      description: "Persist a parent Feature. Reuses an existing Feature with the same normalized title.",
      parameters: featureInput.toJSONSchema(),
      skipPermission: true,
      handler: (args) => create("create_feature", { ...featureInput.parse(args), type: "Feature" }),
    }),
    defineTool("create_user_story", {
      description: "Persist a User Story under an existing Feature, with testable acceptance criteria.",
      parameters: storyInput.toJSONSchema(),
      skipPermission: true,
      handler: (args) => create("create_user_story", { ...storyInput.parse(args), type: "User Story" }),
    }),
    defineTool("create_engineering_task", {
      description: "Persist an engineering Task under an existing User Story. Use a concrete scope and priority.",
      parameters: taskInput.toJSONSchema(),
      skipPermission: true,
      handler: (args) => create("create_engineering_task", { ...taskInput.parse(args), type: "Task" }),
    }),
  ];
  return {
    clarification, creation, calls, questions, tickets,
    enableWrites() {
      if (questions.length) throw new Error("Cannot create tickets while clarification is needed");
      writesEnabled = true;
    },
  };
}
