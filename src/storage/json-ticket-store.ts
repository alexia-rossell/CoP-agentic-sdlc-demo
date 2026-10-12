import { mkdir, open, readFile, rename, unlink } from "node:fs/promises";
import { dirname } from "node:path";
import { randomUUID } from "node:crypto";
import { z } from "zod";
import { ticketSchema, type NewTicket, type Ticket, type TicketStore } from "./tickets.js";

const documentSchema = z.object({
  nextId: z.number().int().min(100).max(Number.MAX_SAFE_INTEGER - 1),
  items: z.array(ticketSchema),
}).strict();
const normalize = (value: string) => value.trim().replace(/\s+/g, " ").toLowerCase();
const parentId = (item: NewTicket | Ticket) => "parentId" in item ? item.parentId : undefined;

function validateHierarchy(items: Ticket[], nextId: number): void {
  const byId = new Map(items.map((item) => [item.id, item]));
  if (byId.size !== items.length) throw new Error("Duplicate IDs in ticket store");
  const keys = new Set<string>();
  for (const item of items) {
    if (Number(item.id.slice(5)) < 100 || Number(item.id.slice(5)) >= nextId) {
      throw new Error("Invalid ticket sequence in store");
    }
    const key = JSON.stringify([item.type, parentId(item), normalize(item.title)]);
    if (keys.has(key)) throw new Error("Duplicate tickets in store");
    keys.add(key);
    if (item.type !== "Feature") {
      const expected = item.type === "User Story" ? "Feature" : "User Story";
      if (byId.get(item.parentId)?.type !== expected) {
        throw new Error(`${item.type} must have a ${expected} parent`);
      }
    }
  }
}

export class JsonTicketStore implements TicketStore {
  private pending: Promise<unknown> = Promise.resolve();

  constructor(private readonly path: string) {}

  private async load() {
    let content: string;
    try {
      content = await readFile(this.path, "utf8");
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === "ENOENT") return { nextId: 100, items: [] as Ticket[] };
      throw error;
    }
    const document = documentSchema.parse(JSON.parse(content));
    validateHierarchy(document.items, document.nextId);
    return document;
  }

  async list(): Promise<Ticket[]> {
    await this.pending;
    return (await this.load()).items;
  }

  create(input: NewTicket): Promise<{ item: Ticket; created: boolean }> {
    const operation = this.pending.then(() => this.write(input));
    this.pending = operation.catch(() => undefined);
    return operation;
  }

  private async write(input: NewTicket): Promise<{ item: Ticket; created: boolean }> {
    await mkdir(dirname(this.path), { recursive: true });
    const lockPath = `${this.path}.lock`;
    // Fail closed if another process owns the store; never overwrite its work.
    const lock = await open(lockPath, "wx", 0o600);
    const temporaryPath = `${this.path}.${randomUUID()}.tmp`;
    try {
      const document = await this.load();
      const item = ticketSchema.parse({ ...input, id: `DEMO-${document.nextId}` });
      const existing = document.items.find((candidate) =>
        candidate.type === item.type &&
        parentId(candidate) === parentId(item) &&
        normalize(candidate.title) === normalize(item.title));
      if (existing) return { item: existing, created: false };
      validateHierarchy([...document.items, item], document.nextId + 1);
      document.items.push(item);
      document.nextId++;
      const file = await open(temporaryPath, "wx", 0o600);
      try {
        await file.writeFile(`${JSON.stringify(document, null, 2)}\n`, "utf8");
        await file.sync();
      } finally {
        await file.close();
      }
      await rename(temporaryPath, this.path);
      return { item, created: true };
    } finally {
      await unlink(temporaryPath).catch((error: NodeJS.ErrnoException) => {
        if (error.code !== "ENOENT") throw error;
      });
      await lock.close();
      await unlink(lockPath);
    }
  }
}
