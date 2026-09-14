using Application.DTOs;
using Application.Interfaces;
using Infrastructure.LLM.Core;
using Microsoft.Extensions.Logging;

namespace Infrastructure.LLM.Services;

public sealed class GeminiVisualIdentityExtractor : IVisualIdentityExtractor
{
    private readonly GeminiApiClient _geminiClient;
    private readonly ILogger<GeminiVisualIdentityExtractor> _logger;

    private const string SystemPrompt =
@"You are an authoritative, evidence-bound visual identity extraction system for a character AI engine.
Your sole mission is to observe the uploaded reference image and extract the subject's persistent visual traits into structured JSON based strictly on observable visual evidence.

CRITICAL EVIDENCE-BOUND INVARIANTS:
1. EVIDENCE-BOUND OBSERVATION ONLY: Report strictly what is visually observable in the image. Never infer unproven facts.
2. NO EXACT MEASUREMENTS: NEVER invent numerical measurements, heights (e.g. '170cm'), weights, clothing sizes, or cup sizes. Use descriptive visual proportions only (e.g., 'slender build', 'long-legged appearance', 'moderate shoulders').
3. NO UNOBSERVABLE ATTRIBUTES: Do NOT guess exact numerical age or specific real-world ethnicity.
4. VISUAL UNCERTAINTY & INSUFFICIENT EVIDENCE: If visual evidence is insufficient (e.g. close-up portrait or headshot where body silhouette/build is not visible), set the corresponding fields to null. Never hallucinate traits without direct visual evidence.
5. SIGNATURE TRAITS: Identify any distinctive permanent visual traits observable, such as horns, pointed ears, distinct beauty marks, characteristic bangs, or scars.

OUTPUT FORMAT:
You must output a single JSON object matching this exact structure:
{
  ""face"": {
    ""shape"": ""e.g., oval / round / angular / delicate (or null if obscured)"",
    ""eyes"": ""color and observable shape, e.g., vivid crimson red almond eyes"",
    ""features"": ""notable observable facial traits, e.g., delicate nose, soft chin""
  },
  ""hair"": {
    ""color"": ""exact observable color, e.g., silver-white / jet black / golden blonde"",
    ""style"": ""e.g., straight with bangs / wavy / twin tails"",
    ""length"": ""e.g., waist-length / shoulder-length / short / cropped""
  },
  ""skin"": {
    ""complexion"": ""e.g., pale porcelain / fair with cool undertones / warm tan""
  },
  ""body"": {
    ""build"": ""e.g., slender / athletic / petite (or null if body is not visible)"",
    ""proportions"": ""e.g., long-legged appearance / narrow waist (or null if not visible)"",
    ""silhouette"": ""e.g., graceful / linear / hourglass (or null if not visible)""
  },
  ""signatureFeatures"": [
    ""array of distinct permanent visual features, e.g., curved black and red horns, small beauty mark below left eye""
  ],
  ""observableGender"": ""Female / Male / Androgynous (or null if ambiguous)""
}";

    public GeminiVisualIdentityExtractor(
        GeminiApiClient geminiClient,
        ILogger<GeminiVisualIdentityExtractor> logger)
    {
        _geminiClient = geminiClient ?? throw new ArgumentNullException(nameof(geminiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VisualIdentityExtractionResult> ExtractIdentityAsync(
        byte[] imageBytes,
        string mimeType,
        CancellationToken ct = default)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image bytes cannot be null or empty.", nameof(imageBytes));
        }

        var normalizedMime = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType.Trim().ToLowerInvariant();
        if (normalizedMime == "image/jpg") normalizedMime = "image/jpeg";

        var base64Data = Convert.ToBase64String(imageBytes);

        var contents = new object[]
        {
            new
            {
                role = "user",
                parts = new object[]
                {
                    new
                    {
                        inline_data = new
                        {
                            mime_type = normalizedMime,
                            data = base64Data
                        }
                    },
                    new
                    {
                        text = "Observe this image with extreme evidence-bound precision and extract the observable visual identity into the required JSON schema."
                    }
                }
            }
        };

        try
        {
            var result = await _geminiClient.GenerateJsonAsync<VisualIdentityExtractionResult>(
                systemPrompt: SystemPrompt,
                contents: contents,
                temperature: 0.2,
                ct: ct);

            if (result == null)
            {
                _logger.LogWarning("Gemini Vision returned empty JSON response for visual identity extraction. Falling back to default empty result.");
                return new VisualIdentityExtractionResult();
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to extract visual identity via Gemini Vision");
            throw new InvalidOperationException($"Vision extraction failed: {ex.Message}", ex);
        }
    }
}
