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

/// <summary>
/// Structured, evidence-bound visual identity observation suggested by AI.
/// Strictly a suggestion DTO for client review and editing; does NOT mutate domain entities.
/// </summary>
public sealed record VisualIdentityExtractionResult(
    ExtractedFace? Face = null,
    ExtractedHair? Hair = null,
    ExtractedSkin? Skin = null,
    ExtractedBody? Body = null,
    List<string>? SignatureFeatures = null,
    string? ObservableGender = null
);

/// <summary>
/// Confirmed visual identity payload submitted by the user after reviewing and editing the suggested identity.
/// </summary>
public sealed record ConfirmedVisualIdentityDto(
    string? Gender = null,
    string? Face = null,
    string? Hair = null,
    string? Eyes = null,
    string? Skin = null,
    string? Body = null,
    string? AgeAppearance = null,
    string? ClothingStyle = null,
    string? Accessories = null,
    string? OriginalReferenceUrl = null,
    string? CanonicalReferenceUrl = null,
    string? FullBodyUrl = null,
    List<string>? SignatureFeatures = null,
    string? Style = null
)
{
    public CharacterVisualIdentity ToDomainEntity()
    {
        List<SignatureFeature>? signatureFeatures = null;
        if (SignatureFeatures != null && SignatureFeatures.Count > 0)
        {
            signatureFeatures = SignatureFeatures
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => new SignatureFeature(f, f, null, FeatureImportance.Critical, FeaturePersistence.EveryTurn))
                .ToList();
        }

        return new CharacterVisualIdentity(
            Gender: Gender,
            Face: Face,
            Hair: Hair,
            Eyes: Eyes,
            Skin: Skin,
            Body: Body,
            AgeAppearance: AgeAppearance,
            ClothingStyle: ClothingStyle,
            Accessories: Accessories,
            OriginalReferenceUrl: OriginalReferenceUrl,
            CanonicalReferenceUrl: CanonicalReferenceUrl,
            FullBodyUrl: FullBodyUrl,
            SignatureFeatures: signatureFeatures,
            Style: Style
        );
    }
}

public sealed record ExtractVisualIdentityResponse(
    string ReferenceImageUrl,
    VisualIdentityExtractionResult ExtractedIdentity
);
