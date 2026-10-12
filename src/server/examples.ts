import { readFile } from "node:fs/promises";
import { z } from "zod";
import type { Example } from "./api-types.js";

const inputSchema = z.object({ name: z.string(), request: z.string().min(1) });
const labels = { incomplete: "Incomplete example", complete: "Complete example" } as const;

// demo-inputs stays the single source of the synthetic scenarios, as for the CLI.
export async function loadExamples(directory = new URL("../../../demo-inputs/", import.meta.url)): Promise<Example[]> {
  return Promise.all((["incomplete", "complete"] as const).map(async (id) => {
    const input = inputSchema.parse(JSON.parse(await readFile(new URL(`${id}.json`, directory), "utf8")));
    return { id, label: labels[id], request: input.request };
  }));
}
