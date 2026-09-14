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
@"You are an authoritative, objective visual identity extraction system for a character AI engine.
Your sole mission is to observe the uploaded reference image and extract the subject's persistent visual traits into structured JSON.

CRITICAL ZERO-HALLUCINATION INVARIANTS:
1. OBJECTIVE OBSERVATION ONLY: Describe strictly what is visibly observable in the image.
2. NO EXACT MEASUREMENTS: NEVER invent numbers, heights (e.g., '170cm'), weights, clothing sizes, or cup sizes. Use descriptive visual proportions only (e.g., 'slender build', 'long legs', 'moderate shoulders').
3. NO UNOBSERVABLE ATTRIBUTES: Do NOT guess exact age or specific real-world ethnicity.
4. PARTIALLY VISIBLE OR HEADSHOT CROPS: If the image is a close-up portrait, headshot, or cropped such that body proportions are not visible, set all Body fields to null. Never hallucinate body traits that cannot be seen.
5. SIGNATURE TRAITS: Identify any permanent, distinctive features such as horns, pointed ears, prominent beauty marks, distinct bangs, scars, or fantasy traits.

OUTPUT FORMAT:
You must output a single JSON object with this exact structure:
{
  ""face"": {
    ""shape"": ""oval / round / angular / delicate"",
    ""eyes"": ""color and observable shape, e.g., vivid red expressive almond eyes"",
    ""features"": ""notable facial traits, e.g., soft chin, high cheekbones, delicate nose""
  },
  ""hair"": {
    ""color"": ""exact observable color, e.g., silver-white / jet black / golden blonde"",
    ""style"": ""e.g., straight with side-swept bangs / wavy / twin tails"",
    ""length"": ""e.g., waist-length / shoulder-length / short / cropped""
  },
  ""skin"": {
    ""complexion"": ""e.g., pale porcelain / fair with cool undertones / warm tan / smooth olive""
  },
  ""body"": {
    ""build"": ""e.g., slender / athletic / muscular / petite (or null if not visible)"",
    ""proportions"": ""e.g., long-legged / balanced proportions / narrow waist (or null if not visible)"",
    ""silhouette"": ""e.g., graceful hourglass / linear slender / broad shoulders (or null if not visible)""
  },
  ""signatureFeatures"": [
    ""array of distinct permanent visual features, e.g., curved black and red demon horns, small beauty mark below left eye""
  ],
  ""visualTraits"": ""concise aesthetic and stylistic impression, e.g., dark fantasy warrior aesthetic"",
  ""observableGender"": ""Female / Male / Androgynous""
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
                        text = "Observe this image with extreme precision and extract the character's visual identity into the required JSON schema."
                    }
                }
            }
        };

        try
        {
            var result = await _geminiClient.GenerateJsonAsync<VisualIdentityExtractionResult>(
                systemPrompt: SystemPrompt,
                contents: contents,
                temperature: 0.2, // Low temperature for maximum objective consistency
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
