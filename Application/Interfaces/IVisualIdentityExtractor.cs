using Application.DTOs;

namespace Application.Interfaces;

/// <summary>
/// Provider-agnostic contract for extracting structured, evidence-bound visual identity observations from a visual reference.
/// </summary>
public interface IVisualIdentityExtractor
{
    Task<VisualIdentityExtractionResult> ExtractIdentityAsync(
        byte[] imageBytes,
        string mimeType,
        CancellationToken ct = default);
}
