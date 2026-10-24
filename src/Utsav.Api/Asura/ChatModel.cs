using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Utsav.Api.Asura;

internal sealed record ChatMessage(string Role, string Content);

internal sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    int MaxTokens = 2_000,
    double Temperature = 0,
    bool ExpectJson = false);

internal sealed record ChatResult(
    string Content,
    string Model,
    string Provider,
    int? TotalTokens,
    long ElapsedMs);

/// <summary>
/// One language model Asura can ask a question of. Deliberately tiny: the
/// orchestrator only ever needs "send these messages, get text back", and
/// keeping the surface this small is what makes <see cref="FallbackChatModel"/>
/// and the unit tests straightforward.
/// </summary>
internal interface IChatModel
{
    string Name { get; }

    Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct);
}

/// <summary>
/// A model call failed in a way that should make the caller try the next tier.
/// </summary>
internal sealed class ChatModelException(string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>
    /// True for a failure that might succeed if tried again shortly — a rate
    /// limit, a 5xx, a timeout. Distinguished from a 401 or a 404, where
    /// retrying only burns quota and delays the real answer.
    /// </summary>
    public bool IsTransient { get; init; }

    /// <summary>
    /// True when the cause is configuration rather than a provider being down.
    ///
    /// <para>
    /// The two need opposite responses — one is "wait and retry", the other is
    /// "add a setting" — so the orchestrator shows this message verbatim
    /// instead of its generic "can't reach the models" text. Safe to surface:
    /// Asura is treasurer-level only, and the message names settings, never
    /// values.
    /// </para>
    /// </summary>
    public bool IsConfiguration { get; init; }

    /// <summary>Per-tier failure reasons, for the UI's "why" disclosure.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// True for HTTP 401/403 — the provider received and parsed the request
    /// and rejected it specifically for its credentials. Distinguished from
    /// a transport failure or a 5xx because the right top-level message is
    /// opposite: "the key is wrong", not "we couldn't reach the model". A
    /// real incident shipped with the latter wording while every provider
    /// was in fact returning a clean 401 — technically true in the sense
    /// that no answer came back, but it sent the search toward a network
    /// theory when the request had plainly arrived and been read.
    /// </summary>
    public bool IsAuthFailure { get; init; }

    /// <summary>
    /// Set on the final "every tier failed" exception when every underlying
    /// failure was an auth rejection. Lets the orchestrator say the accurate
    /// thing — the keys are wrong — instead of the generic unreachable
    /// message once every tier's cause is known.
    /// </summary>
    public bool AllTiersRejectedAuth { get; init; }
}

/// <summary>
/// Talks to any OpenAI-shaped chat-completions endpoint. Both providers Asura
/// uses — Groq and NVIDIA NIM — speak this protocol, so the difference between
/// them is configuration, not code.
/// </summary>
internal sealed class OpenAiCompatibleChatModel(
    HttpClient http,
    AsuraProviderOptions options,
    ILogger<OpenAiCompatibleChatModel> log) : IChatModel
{
    /// <summary>
    /// Floor on <c>max_tokens</c>, and it is not arbitrary. <c>gpt-oss-*</c> are
    /// reasoning models: they spend tokens thinking before they emit any
    /// content, and the thinking is billed against the same budget. Probed with
    /// <c>max_tokens: 16</c>, <c>gpt-oss-120b</c> returned HTTP 200, a
    /// <c>reasoning_tokens: 14</c> usage block, and a completely <b>empty</b>
    /// message — which reads like a broken model rather than a budget that was
    /// too small. Anything below this and the failure is silent.
    /// </summary>
    private const int MinMaxTokens = 1_500;

    public string Name => $"{options.Provider}/{options.Model}";

    public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["messages"] = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            ["temperature"] = request.Temperature,
            ["max_tokens"] = Math.Max(request.MaxTokens, MinMaxTokens),
        };

        // Not every OpenAI-compatible provider honours response_format, so it is
        // a hint rather than the guarantee. The orchestrator still has to cope
        // with a fenced or prose-wrapped object, which is why ExtractJson exists.
        if (request.ExpectJson)
        {
            payload["response_format"] = new { type = "json_object" };
        }

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            // Building the request is inside this try deliberately, alongside
            // sending it. An invalid API key — notably one with a trailing
            // newline or space from a Portal copy-paste — makes
            // AuthenticationHeaderValue's constructor throw a FormatException
            // before any network call happens. That exception is not a
            // ChatModelException, so if it were thrown outside this block it
            // would escape FallbackChatModel's catch entirely: not advance to
            // the next tier, not appear in the per-tier Detail, just an
            // unhandled 500. One try covering construction and sending means
            // every way this tier can fail becomes the same kind of failure.
            using var message = new HttpRequestMessage(HttpMethod.Post, options.BaseUrl)
            {
                Content = JsonContent.Create(payload),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

            response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller gave up; this is not a model failure
        }
        catch (ChatModelException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Covers both a transport failure (DNS, TLS, connect, read --
            // exactly what blocked egress or a flaky network looks like, and
            // exactly what is worth one retry) and a malformed-key
            // FormatException from header construction. The two need
            // different IsTransient values: retrying a bad key fails
            // identically, where a network blip might clear.
            throw new ChatModelException($"{Name}: {ex.GetType().Name} — {ex.Message}", ex)
            {
                IsTransient = ex is not FormatException,
            };
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // 403 with Cloudflare's "error code: 1010" is the signature of a
                // missing or rejected User-Agent, not a bad key — see
                // AsuraOptions for why the header is mandatory. Called out
                // explicitly because the symptom (403 on every completion while
                // GET /v1/models still works) otherwise sends you hunting for a
                // credentials problem that isn't there.
                var hint = response.StatusCode == HttpStatusCode.Forbidden && body.Contains("1010")
                    ? " (Cloudflare 1010 — the User-Agent header is missing or blocked, not the API key)"
                    : "";
                throw new ChatModelException(
                    $"{Name}: HTTP {(int)response.StatusCode}{hint} — {Truncate(body, 300)}")
                {
                    IsTransient = response.StatusCode is HttpStatusCode.TooManyRequests
                        or HttpStatusCode.RequestTimeout
                        or HttpStatusCode.BadGateway
                        or HttpStatusCode.ServiceUnavailable
                        or HttpStatusCode.GatewayTimeout
                        || (int)response.StatusCode >= 500,
                    IsAuthFailure = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                };
            }

            string? content;
            int? tokens;
            try
            {
                var parsed = JsonSerializer.Deserialize<CompletionResponse>(body);
                content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
                tokens = parsed?.Usage?.TotalTokens;
            }
            catch (JsonException ex)
            {
                throw new ChatModelException($"{Name}: unreadable response body", ex);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                // Usually the reasoning-token trap described above, so say so
                // rather than reporting a bare "empty response".
                throw new ChatModelException(
                    $"{Name}: returned no content (a reasoning model may have spent the whole " +
                    $"max_tokens budget thinking — floor is {MinMaxTokens})");
            }

            log.LogDebug("Asura model {Model} answered in {Ms}ms using {Tokens} tokens",
                Name, sw.ElapsedMilliseconds, tokens);

            return new ChatResult(content, options.Model, options.Provider, tokens, sw.ElapsedMilliseconds);
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    // Only the handful of fields Asura actually reads.
    private sealed record CompletionResponse(
        [property: JsonPropertyName("choices")] List<Choice>? Choices,
        [property: JsonPropertyName("usage")] Usage? Usage);

    private sealed record Choice([property: JsonPropertyName("message")] ResponseMessage? Message);

    private sealed record ResponseMessage([property: JsonPropertyName("content")] string? Content);

    private sealed record Usage([property: JsonPropertyName("total_tokens")] int? TotalTokens);
}

/// <summary>
/// Tries each configured model in order and returns the first answer.
///
/// <para>
/// Two providers, not two models from one provider, and that is the point: the
/// probe on 2026-10-05 found Groq's whole estate behind one Cloudflare edge and
/// a shared free-tier token-per-minute budget, so a second Groq model is no
/// protection against the failure that actually happens. Tier 2 is NVIDIA NIM —
/// a different company, different network, different quota. Tier 3 returns to
/// Groq on a different model, which at least has its own rate-limit bucket.
/// </para>
/// </summary>
internal sealed class FallbackChatModel(
    IReadOnlyList<IChatModel> tiers,
    ILogger<FallbackChatModel> log) : IChatModel
{
    public string Name => string.Join(" → ", tiers.Select(t => t.Name));

    /// <summary>Which tier (1-based) answered the last successful call, for the UI's provenance line.</summary>
    public int LastTierUsed { get; private set; }

    public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        if (tiers.Count == 0)
        {
            throw new ChatModelException(
                "Asura has no model configured yet. Set Asura__GroqApiKey and Asura__NvidiaApiKey "
                + "in the App Service application settings, then restart the app.")
            {
                IsConfiguration = true,
            };
        }

        var failures = new List<string>();
        var allAuthFailuresSoFar = true;

        for (var i = 0; i < tiers.Count; i++)
        {
            // A caller who navigated away must not cause the next provider to
            // be hammered: cancellation is not a model failure, so it is
            // checked before every attempt and rethrown rather than absorbed.
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await CompleteWithRetryAsync(tiers[i], request, ct);
                LastTierUsed = i + 1;
                if (i > 0)
                {
                    log.LogWarning(
                        "Asura fell through to tier {Tier} ({Model}); earlier tiers failed: {Failures}",
                        i + 1, tiers[i].Name, string.Join(" | ", failures));
                }

                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ChatModelException ex)
            {
                failures.Add(ex.Message);
                allAuthFailuresSoFar &= ex.IsAuthFailure;
            }
        }

        throw new ChatModelException(
            "Every Asura model tier failed: " + string.Join(" | ", failures))
        {
            Detail = string.Join("\n", failures),
            AllTiersRejectedAuth = allAuthFailuresSoFar,
        };
    }

    /// <summary>
    /// One retry for a transient failure before giving up on this tier.
    ///
    /// <para>
    /// A rate limit or a 503 is often gone a second later, and moving straight
    /// to the next provider on a blip means a slower answer from a weaker model
    /// for no reason. Non-transient failures — a bad key, an unknown model —
    /// are not retried, since the second attempt fails identically and only
    /// delays the real error.
    /// </para>
    /// </summary>
    private static async Task<ChatResult> CompleteWithRetryAsync(
        IChatModel tier, ChatRequest request, CancellationToken ct)
    {
        try
        {
            return await tier.CompleteAsync(request, ct);
        }
        catch (ChatModelException ex) when (ex.IsTransient && !ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
            return await tier.CompleteAsync(request, ct);
        }
    }
}
