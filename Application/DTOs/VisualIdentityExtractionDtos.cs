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

public sealed record ConfirmedSignatureFeatureDto(
    string Name,
    string? PositiveTokens = null,
    FeatureImportance Importance = FeatureImportance.High,
    FeaturePersistence Persistence = FeaturePersistence.EveryTurn
)
{
    public static implicit operator ConfirmedSignatureFeatureDto(string name) => new(name, name);
}

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
    string? CanonicalFaceReferenceUrl = null,
    string? CanonicalBodyReferenceUrl = null,
    List<ConfirmedSignatureFeatureDto>? SignatureFeatures = null,
    string? Style = null
)
{
    [Obsolete("Use CanonicalFaceReferenceUrl instead.")]
    public string? CanonicalReferenceUrl => CanonicalFaceReferenceUrl;

    [Obsolete("Use CanonicalBodyReferenceUrl instead.")]
    public string? FullBodyUrl => CanonicalBodyReferenceUrl;

    public ConfirmedVisualIdentityDto(
        string? gender = null,
        string? face = null,
        string? hair = null,
        string? eyes = null,
        string? skin = null,
        string? body = null,
        string? ageAppearance = null,
        string? clothingStyle = null,
        string? accessories = null,
        string? originalReferenceUrl = null,
        string? canonicalFaceReferenceUrl = null,
        string? canonicalBodyReferenceUrl = null,
        List<string>? signatureFeatures = null,
        string? style = null,
        string? canonicalReferenceUrl = null,
        string? fullBodyUrl = null)
        : this(
            gender,
            face,
            hair,
            eyes,
            skin,
            body,
            ageAppearance,
            clothingStyle,
            accessories,
            originalReferenceUrl,
            canonicalFaceReferenceUrl ?? canonicalReferenceUrl,
            canonicalBodyReferenceUrl ?? fullBodyUrl,
            signatureFeatures?.Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => new ConfirmedSignatureFeatureDto(f, f, FeatureImportance.High, FeaturePersistence.EveryTurn))
                .ToList(),
            style)
    {
    }

    public CharacterVisualIdentity ToDomainEntity()
    {
        List<SignatureFeature>? signatureFeatures = null;
        if (SignatureFeatures != null && SignatureFeatures.Count > 0)
        {
            signatureFeatures = SignatureFeatures
                .Where(f => !string.IsNullOrWhiteSpace(f?.Name))
                .Select(f => new SignatureFeature(
                    f.Name,
                    !string.IsNullOrWhiteSpace(f.PositiveTokens) ? f.PositiveTokens : f.Name,
                    null,
                    f.Importance,
                    f.Persistence))
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
            CanonicalFaceReferenceUrl: CanonicalFaceReferenceUrl,
            CanonicalBodyReferenceUrl: CanonicalBodyReferenceUrl,
            SignatureFeatures: signatureFeatures,
            Style: Style
        );
    }
}

public sealed record ExtractVisualIdentityResponse(
    string ReferenceImageUrl,
    VisualIdentityExtractionResult ExtractedIdentity
);
