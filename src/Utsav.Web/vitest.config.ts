import { defineConfig } from "vitest/config";
import path from "path";

// Pure-logic tests only (colour maths and the like); nothing here needs a DOM.
export default defineConfig({
  resolve: { alias: { "@": path.resolve(__dirname, "./src") } },
  test: { environment: "node", include: ["src/**/*.test.ts"] },
});
