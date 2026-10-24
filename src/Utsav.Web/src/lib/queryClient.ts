import { QueryClient } from "@tanstack/react-query";
import { ApiError } from "./api";

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      // Retry only what a retry could fix. A 401/403/404 means the answer
      // won't change, so repeating it just delays the error the user needs
      // to see. A 5xx or a dropped connection, on the other hand, is often
      // the serverless function cold-starting or the database pooler
      // briefly full — the server already backs off internally, and this is
      // the last line of defence behind it.
      retry: (failureCount, error) => {
        if (error instanceof ApiError && error.status < 500) return false;
        return failureCount < 2;
      },
      retryDelay: (attempt) => Math.min(1500, 400 * 2 ** attempt),
    },
  },
});
