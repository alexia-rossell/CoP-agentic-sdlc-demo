import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { z } from "zod";
import { runCopilot } from "../src/agent/run-agent.js";
import { JsonTicketStore } from "../src/storage/json-ticket-store.js";
import { casesSchema, evaluateResult } from "./expectations.js";

async function main() {
  const cases = casesSchema.parse(JSON.parse(await readFile(
    new URL("../../evaluations/cases.json", import.meta.url), "utf8",
  )));
  let failed = 0;
  for (const testCase of cases) {
    const directory = await mkdtemp(join(tmpdir(), "agentic-sdlc-evaluation-"));
    try {
      const input = z.object({ request: z.string() }).parse(JSON.parse(await readFile(
        new URL(`../../demo-inputs/${testCase.input}.json`, import.meta.url), "utf8",
      )));
      const store = new JsonTicketStore(join(directory, "tickets.json"));
      const result = await runCopilot(input.request, store, process.env.COPILOT_MODEL);
      const failures = evaluateResult(testCase, result);
      if (testCase.expectedStatus === "needs_information" && (await store.list()).length) {
        failures.push("Incomplete request wrote tickets to disk");
      }
      console.log(`${failures.length ? "FAIL" : "PASS"}: ${testCase.name}`);
      console.log(`Tools: ${result.calls.map((call) => `${call.name}:${call.success ? "ok" : "failed"}`).join(", ")}`);
      for (const failure of failures) console.error(`  ${failure}`);
      if (failures.length) failed++;
    } catch (error) {
      failed++;
      console.error(`FAIL: ${testCase.name}: ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      await rm(directory, { recursive: true, force: true });
    }
  }
  if (failed) process.exitCode = 1;
}

main().catch((error: unknown) => {
  console.error(error);
  process.exitCode = 1;
});
