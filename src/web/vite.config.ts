import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

const outDir = process.env.SKILLHORIZON_WEB_OUT ?? "../hosts/SkillHorizon.Web/wwwroot";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir,
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      "/api": "http://127.0.0.1:8080",
      "/signin/oidc": "http://127.0.0.1:8080",
      "/health": "http://127.0.0.1:8080",
    },
  },
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
});
