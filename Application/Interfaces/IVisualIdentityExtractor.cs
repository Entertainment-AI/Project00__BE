using Application.DTOs;

namespace Application.Interfaces;

/// <summary>
/// Provider-agnostic contract for extracting structured, zero-hallucination visual identity invariants from a visual reference.
/// </summary>
public interface IVisualIdentityExtractor
{
    Task<VisualIdentityExtractionResult> ExtractIdentityAsync(
        byte[] imageBytes,
        string mimeType,
        CancellationToken ct = default);
}
