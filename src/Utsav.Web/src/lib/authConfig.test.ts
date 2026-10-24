import { describe, expect, it } from "vitest";
import { mergeAuthConfig } from "./authConfig";

describe("mergeAuthConfig", () => {
  it("prefers what the running server reports", () => {
    const merged = mergeAuthConfig(
      { google_client_id: "server-google", entra_client_id: "server-entra", entra_tenant_id: "server-tenant" },
      { google: "env-google", entraClient: "env-entra", entraTenant: "env-tenant" },
    );
    expect(merged).toEqual({ googleClientId: "server-google", entraClientId: "server-entra", entraTenantId: "server-tenant" });
  });

  it("falls back to build-time values for local development", () => {
    const merged = mergeAuthConfig(
      { google_client_id: null, entra_client_id: null, entra_tenant_id: null },
      { google: "env-google", entraClient: undefined, entraTenant: undefined },
    );
    expect(merged).toEqual({ googleClientId: "env-google", entraClientId: null, entraTenantId: null });
  });

  it("treats blank values as not configured", () => {
    const merged = mergeAuthConfig(
      { google_client_id: "  ", entra_client_id: "", entra_tenant_id: null },
      { google: "", entraClient: "", entraTenant: "" },
    );
    expect(merged).toEqual({ googleClientId: null, entraClientId: null, entraTenantId: null });
  });

  it("reports nothing configured when the server could not be reached", () => {
    expect(mergeAuthConfig(null, {})).toEqual({ googleClientId: null, entraClientId: null, entraTenantId: null });
  });
});
