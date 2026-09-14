using Domain.Enums;
using Domain.ValueObjects;

namespace Application.DTOs;

public sealed record ExtractedFace(
    string? Shape = null,
    string? Eyes = null,
    string? Features = null
);

public sealed record ExtractedHair(
    string? Color = null,
    string? Style = null,
    string? Length = null
);

public sealed record ExtractedSkin(
    string? Complexion = null
);

public sealed record ExtractedBody(
    string? Build = null,
    string? Proportions = null,
    string? Silhouette = null
);

public sealed record VisualIdentityExtractionResult(
    ExtractedFace? Face = null,
    ExtractedHair? Hair = null,
    ExtractedSkin? Skin = null,
    ExtractedBody? Body = null,
    List<string>? SignatureFeatures = null,
    string? VisualTraits = null,
    string? ObservableGender = null
)
{
    public CharacterVisualIdentity ToCharacterVisualIdentity(
        string? canonicalReferenceUrl = null,
        string? fullBodyUrl = null,
        string? style = null)
    {
        var hairTokens = new List<string>();
        if (!string.IsNullOrWhiteSpace(Hair?.Length)) hairTokens.Add(Hair.Length);
        if (!string.IsNullOrWhiteSpace(Hair?.Color)) hairTokens.Add(Hair.Color);
        if (!string.IsNullOrWhiteSpace(Hair?.Style)) hairTokens.Add(Hair.Style);
        if (hairTokens.Count > 0) hairTokens.Add("hair");
        var compiledHair = hairTokens.Count > 0 ? string.Join(" ", hairTokens) : null;

        var faceTokens = new List<string>();
        if (!string.IsNullOrWhiteSpace(Face?.Shape)) faceTokens.Add($"{Face.Shape} face");
        if (!string.IsNullOrWhiteSpace(Face?.Features)) faceTokens.Add(Face.Features);
        var compiledFace = faceTokens.Count > 0 ? string.Join(", ", faceTokens) : null;

        var bodyTokens = new List<string>();
        if (!string.IsNullOrWhiteSpace(Body?.Build)) bodyTokens.Add(Body.Build);
        if (!string.IsNullOrWhiteSpace(Body?.Proportions)) bodyTokens.Add(Body.Proportions);
        if (!string.IsNullOrWhiteSpace(Body?.Silhouette)) bodyTokens.Add(Body.Silhouette);
        var compiledBody = bodyTokens.Count > 0 ? string.Join(", ", bodyTokens) : null;

        List<SignatureFeature>? signatureFeatures = null;
        if (SignatureFeatures != null && SignatureFeatures.Count > 0)
        {
            signatureFeatures = SignatureFeatures
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => new SignatureFeature(f, f, null, FeatureImportance.Critical, FeaturePersistence.EveryTurn))
                .ToList();
        }

        return new CharacterVisualIdentity(
            Gender: ObservableGender,
            Face: compiledFace,
            Hair: compiledHair,
            Eyes: Face?.Eyes,
            Skin: Skin?.Complexion,
            Body: compiledBody,
            VisualTraits: VisualTraits,
            CanonicalReferenceUrl: canonicalReferenceUrl,
            FullBodyUrl: fullBodyUrl,
            SignatureFeatures: signatureFeatures,
            Style: style
        );
    }
}

public sealed record ExtractVisualIdentityResponse(
    string ReferenceImageUrl,
    VisualIdentityExtractionResult ExtractedIdentity
);
