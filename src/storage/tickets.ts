import { z } from "zod";

const text = z.string().trim().min(1).max(4000);
const id = z.string().regex(/^DEMO-\d+$/);
export const featureInput = z.object({
  title: text,
  description: text,
  businessValue: text,
  tags: z.array(text).min(1),
}).strict();
export const storyInput = z.object({
  parentId: id,
  title: text,
  persona: text,
  goal: text,
  benefit: text,
  acceptanceCriteria: z.array(text).min(1),
}).strict();
export const taskInput = z.object({
  parentId: id,
  title: text,
  description: text,
  category: z.enum(["implementation", "security", "testing", "observability", "operations"]),
  priority: z.enum(["high", "medium", "low"]),
}).strict();

export const ticketSchema = z.discriminatedUnion("type", [
  featureInput.extend({ id, type: z.literal("Feature") }),
  storyInput.extend({ id, type: z.literal("User Story") }),
  taskInput.extend({ id, type: z.literal("Task") }),
]);
export type Ticket = z.infer<typeof ticketSchema>;
export type FeatureInput = z.infer<typeof featureInput>;
export type StoryInput = z.infer<typeof storyInput>;
export type TaskInput = z.infer<typeof taskInput>;
export type NewTicket =
  | (FeatureInput & { type: "Feature" })
  | (StoryInput & { type: "User Story" })
  | (TaskInput & { type: "Task" });

export interface TicketStore {
  list(): Promise<Ticket[]>;
  create(input: NewTicket): Promise<{ item: Ticket; created: boolean }>;
}
