using Application.DTOs;
using Application.Features.Characters.Commands.ExtractVisualIdentity;
using Application.Interfaces;
using Domain.Enums;
using Domain.ValueObjects;
using Infrastructure.LLM;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.VisualIdentity;

public sealed class VisualIdentityExtractorTests
{
    private sealed class StubStorageService : IStorageService
    {
        public Task<string> SaveImageAsync(byte[] imageBytes, string fileName, string contentType = "image/jpeg", CancellationToken ct = default)
        {
            return Task.FromResult($"/uploads/stub_{fileName}");
        }

        public Task<string> SaveBase64ImageAsync(string base64Data, string fileName, CancellationToken ct = default)
        {
            return Task.FromResult($"/uploads/stub_{fileName}");
        }

        public Task<bool> DeleteFileAsync(string fileUrl, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<byte[]?> ReadImageBytesAsync(string fileUrl, CancellationToken ct = default)
        {
            return Task.FromResult<byte[]?>(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02 });
        }
    }

    private sealed class StubExtractor : IVisualIdentityExtractor
    {
        private readonly VisualIdentityExtractionResult _result;

        public StubExtractor(VisualIdentityExtractionResult result)
        {
            _result = result;
        }

        public Task<VisualIdentityExtractionResult> ExtractIdentityAsync(byte[] imageBytes, string mimeType, CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingImageGenerationService : IImageGenerationService
    {
        public List<ImageGenerationRequest> RecordedRequests { get; } = new();

        public Task<string> GenerateImageAsync(string prompt, int width = 512, int height = 512, CancellationToken ct = default)
        {
            return Task.FromResult("https://cdn.project00.ai/gen_avatar.png");
        }

        public Task<string> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
        {
            RecordedRequests.Add(request);
            var isFullBody = request.Height > request.Width;
            var url = isFullBody ? "https://cdn.project00.ai/gen_fullbody.png" : "https://cdn.project00.ai/gen_avatar.png";
            return Task.FromResult(url);
        }

        public Task<ImageGenerationResult> GenerateImageWithResultAsync(ImageGenerationRequest request, CancellationToken ct = default)
        {
            RecordedRequests.Add(request);
            var isFullBody = request.Height > request.Width;
            var url = isFullBody ? "https://cdn.project00.ai/gen_fullbody.png" : "https://cdn.project00.ai/gen_avatar.png";
            return Task.FromResult(new ImageGenerationResult(url, "Stub", "job_1", 100, 12345));
        }
    }

    [Fact]
    public void ConfirmedVisualIdentityDto_Maps_All_Confirmed_Fields_To_Domain_Entity()
    {
        var confirmed = new ConfirmedVisualIdentityDto(
            Gender: "Female",
            Face: "oval face, vivid crimson red almond eyes, soft delicate chin",
            Hair: "waist-length silver-white straight hair",
            Eyes: "vivid crimson red almond eyes",
            Skin: "pale porcelain",
            Body: "slender build, long-legged appearance",
            AgeAppearance: "young adult",
            ClothingStyle: "dark fantasy armor",
            Accessories: "silver necklace",
            OriginalReferenceUrl: "https://cdn.project00.ai/original_upload.png",
            CanonicalFaceReferenceUrl: "https://cdn.project00.ai/face_ref.png",
            CanonicalBodyReferenceUrl: "https://cdn.project00.ai/fullbody_ref.png",
            SignatureFeatures: new List<ConfirmedSignatureFeatureDto>
            {
                new("curved black and red dragon horns", "curved black and red dragon horns", FeatureImportance.High, FeaturePersistence.EveryTurn),
                new("pointy ears", "pointy ears", FeatureImportance.Contextual, FeaturePersistence.SameSceneOnly)
            },
            Style: "Anime"
        );

        var identity = confirmed.ToDomainEntity();

        Assert.Equal("Female", identity.Gender);
        Assert.Equal("vivid crimson red almond eyes", identity.Eyes);
        Assert.Equal("waist-length silver-white straight hair", identity.Hair);
        Assert.Equal("pale porcelain", identity.Skin);
        Assert.Equal("slender build, long-legged appearance", identity.Body);
        Assert.Equal("https://cdn.project00.ai/original_upload.png", identity.OriginalReferenceUrl);
        Assert.Equal("https://cdn.project00.ai/face_ref.png", identity.CanonicalFaceReferenceUrl);
        Assert.Equal("https://cdn.project00.ai/face_ref.png", identity.CanonicalReferenceUrl); // backward compatibility
        Assert.Equal("https://cdn.project00.ai/fullbody_ref.png", identity.CanonicalBodyReferenceUrl);
        Assert.Equal("https://cdn.project00.ai/fullbody_ref.png", identity.FullBodyUrl); // backward compatibility
        Assert.Equal(2, identity.SignatureFeatures?.Count);
        Assert.Equal("curved black and red dragon horns", identity.SignatureFeatures?[0].Name);
        Assert.Equal(FeatureImportance.High, identity.SignatureFeatures?[0].Importance);
        Assert.Equal(FeaturePersistence.EveryTurn, identity.SignatureFeatures?[0].Persistence);
        Assert.Equal(FeatureImportance.Contextual, identity.SignatureFeatures?[1].Importance);
        Assert.Equal(FeaturePersistence.SameSceneOnly, identity.SignatureFeatures?[1].Persistence);
    }

    [Fact]
    public void ConfirmedVisualIdentityDto_With_Simple_Strings_Defaults_To_High_Importance_Not_Critical()
    {
        var confirmed = new ConfirmedVisualIdentityDto(
            gender: "Female",
            signatureFeatures: new List<string> { "freckles" }
        );

        var identity = confirmed.ToDomainEntity();

        Assert.NotNull(identity.SignatureFeatures);
        Assert.Single(identity.SignatureFeatures);
        Assert.Equal("freckles", identity.SignatureFeatures[0].Name);
        Assert.Equal(FeatureImportance.High, identity.SignatureFeatures[0].Importance);
    }

    [Fact]
    public void ExtractionResult_Allows_Uncertainty_With_Null_Fields()
    {
        // For a close-up headshot where body is not visible
        var result = new VisualIdentityExtractionResult(
            Face: new ExtractedFace("round", "emerald green eyes", null),
            Hair: new ExtractedHair("auburn", "curly", "shoulder-length"),
            Skin: new ExtractedSkin("freckled fair"),
            Body: new ExtractedBody(null, null, null), // Uncertain / not visible
            SignatureFeatures: null,
            ObservableGender: "Female"
        );

        Assert.NotNull(result.Face);
        Assert.Null(result.Body?.Build);
        Assert.Null(result.Body?.Proportions);
        Assert.Null(result.Body?.Silhouette);
        Assert.Null(result.SignatureFeatures);
    }

    [Fact]
    public async Task ExtractVisualIdentityHandler_Rejects_Empty_Image_Stream()
    {
        var storage = new StubStorageService();
        var extractor = new StubExtractor(new VisualIdentityExtractionResult());
        var handler = new ExtractVisualIdentityHandler(storage, extractor, NullLogger<ExtractVisualIdentityHandler>.Instance);

        using var emptyStream = new MemoryStream(Array.Empty<byte>());
        var command = new ExtractVisualIdentityCommand(ImageStream: emptyStream, FileName: "empty.jpg", ContentType: "image/jpeg");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("empty", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractVisualIdentityHandler_Rejects_Oversized_Image_Stream()
    {
        var storage = new StubStorageService();
        var extractor = new StubExtractor(new VisualIdentityExtractionResult());
        var handler = new ExtractVisualIdentityHandler(storage, extractor, NullLogger<ExtractVisualIdentityHandler>.Instance);

        // 10 MB + 1 byte
        var oversizedBytes = new byte[10 * 1024 * 1024 + 1];
        using var stream = new MemoryStream(oversizedBytes);
        var command = new ExtractVisualIdentityCommand(ImageStream: stream, FileName: "large.jpg", ContentType: "image/jpeg");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("10 MB", result.Errors[0]);
    }

    [Fact]
    public async Task ExtractVisualIdentityHandler_Successfully_Saves_Image_And_Extracts_Identity()
    {
        var expectedIdentity = new VisualIdentityExtractionResult(
            Face: new ExtractedFace("round", "blue eyes", null),
            Hair: new ExtractedHair("blonde", "short", "shoulder-length"),
            Skin: new ExtractedSkin("fair"),
            Body: new ExtractedBody("athletic", "medium", null),
            SignatureFeatures: new List<string> { "freckles" }
        );

        var storage = new StubStorageService();
        var extractor = new StubExtractor(expectedIdentity);
        var handler = new ExtractVisualIdentityHandler(storage, extractor, NullLogger<ExtractVisualIdentityHandler>.Instance);

        var sampleBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02 }; // Fake JPEG
        using var stream = new MemoryStream(sampleBytes);
        var command = new ExtractVisualIdentityCommand(ImageStream: stream, FileName: "avatar.jpg", ContentType: "image/jpeg");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.StartsWith("/uploads/stub_avatar.jpg", result.Value.ReferenceImageUrl);
        Assert.Equal("blonde", result.Value.ExtractedIdentity.Hair?.Color);
        Assert.Equal("athletic", result.Value.ExtractedIdentity.Body?.Build);
    }

    private sealed class ThrowingExtractor : IVisualIdentityExtractor
    {
        public Task<VisualIdentityExtractionResult> ExtractIdentityAsync(byte[] imageBytes, string mimeType, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Internal API key quota exceeded sensitive details");
        }
    }

    [Fact]
    public async Task ExtractVisualIdentityHandler_Does_Not_Expose_Exception_Message_To_Client()
    {
        var storage = new StubStorageService();
        var extractor = new ThrowingExtractor();
        var handler = new ExtractVisualIdentityHandler(storage, extractor, NullLogger<ExtractVisualIdentityHandler>.Instance);

        var sampleBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02 };
        using var stream = new MemoryStream(sampleBytes);
        var command = new ExtractVisualIdentityCommand(ImageStream: stream, FileName: "avatar.jpg", ContentType: "image/jpeg");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.StatusCode);
        Assert.DoesNotContain("sensitive details", result.Errors[0]);
        Assert.Equal("Failed to process reference image.", result.Errors[0]);
    }

    [Fact]
    public async Task GenerateAvatarAsync_With_ReferenceImageUrl_Uses_IdentityConditioning_Abstraction()
    {
        var imageService = new RecordingImageGenerationService();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var httpClient = new System.Net.Http.HttpClient();
        var geminiClient = new Infrastructure.LLM.Core.GeminiApiClient(
            httpClient,
            config,
            NullLogger<Infrastructure.LLM.Core.GeminiApiClient>.Instance);

        var promptCompiler = new Infrastructure.LLM.Prompts.PromptCompiler();
        var llmService = new LLMService(
            geminiClient,
            imageService,
            promptCompiler);

        var request = new GenerateAvatarRequest(
            name: "Lyra",
            title: "Dragon Sovereign",
            category: "Fantasy",
            idea: "Silver-haired dragon princess with crimson eyes",
            referenceImageUrl: "https://cdn.project00.ai/user_uploaded_reference.png"
        );

        var response = await llmService.GenerateAvatarAsync(request, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(imageService.RecordedRequests);

        var avatarReq = imageService.RecordedRequests[0];
        // Must NOT hardcode workflow or IP-Adapter parameters in LLMService!
        Assert.Null(avatarReq.Workflow);
        Assert.Null(avatarReq.ParametersJson);
        Assert.Equal("https://cdn.project00.ai/user_uploaded_reference.png", avatarReq.ReferenceImageUrl);
        Assert.Equal(512, avatarReq.Width);
        Assert.Equal(512, avatarReq.Height);

        // Uses IdentityConditioning abstraction:
        Assert.NotNull(avatarReq.IdentityConditioning);
        Assert.True(avatarReq.IdentityConditioning.IsRequired);
        Assert.Equal(0.65f, avatarReq.IdentityConditioning.PreservationStrength);
        Assert.Equal("https://cdn.project00.ai/user_uploaded_reference.png", avatarReq.IdentityConditioning.CanonicalReferenceUrl);

        // Automatically resolves to VisualIdentity v1 via capability resolution:
        var resolvedCapability = avatarReq.ResolveEffectiveCapability();
        Assert.Equal("VisualIdentity", resolvedCapability.Workflow);
        Assert.Equal(1, resolvedCapability.WorkflowVersion);

        Assert.Equal("https://cdn.project00.ai/gen_avatar.png", response.AvatarUrl);
        Assert.Null(response.FullBodyUrl); // Decoupled!
    }

    [Fact]
    public async Task GenerateStandeeAsync_Prioritizes_OriginalReference_Over_Avatar_For_Body_Evidence()
    {
        var imageService = new RecordingImageGenerationService();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var httpClient = new System.Net.Http.HttpClient();
        var geminiClient = new Infrastructure.LLM.Core.GeminiApiClient(
            httpClient,
            config,
            NullLogger<Infrastructure.LLM.Core.GeminiApiClient>.Instance);

        var promptCompiler = new Infrastructure.LLM.Prompts.PromptCompiler();
        var llmService = new LLMService(
            geminiClient,
            imageService,
            promptCompiler);

        // When BOTH Avatar (face crop) and ReferenceImageUrl (uploaded original with body evidence) are provided:
        var standeeRequest = new GenerateStandeeRequest(
            name: "Lyra",
            title: "Dragon Sovereign",
            category: "Fantasy",
            avatarUrl: "https://cdn.project00.ai/gen_avatar_face_crop.png",
            referenceImageUrl: "https://cdn.project00.ai/user_uploaded_original_reference.png"
        );

        var response = await llmService.GenerateStandeeAsync(standeeRequest, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(imageService.RecordedRequests);

        var standeeReq = imageService.RecordedRequests[0];
        // Must NOT hardcode workflow or IP-Adapter parameters:
        Assert.Null(standeeReq.Workflow);
        Assert.Null(standeeReq.ParametersJson);

        // MUST prioritize Original Reference over Avatar to avoid identity drift:
        Assert.Equal("https://cdn.project00.ai/user_uploaded_original_reference.png", standeeReq.ReferenceImageUrl);
        Assert.Equal(512, standeeReq.Width);
        Assert.Equal(768, standeeReq.Height);

        // Uses IdentityConditioning abstraction:
        Assert.NotNull(standeeReq.IdentityConditioning);
        Assert.True(standeeReq.IdentityConditioning.IsRequired);
        Assert.Equal(0.45f, standeeReq.IdentityConditioning.PreservationStrength);

        // Automatically resolves to VisualIdentity v1:
        var resolvedCapability = standeeReq.ResolveEffectiveCapability();
        Assert.Equal("VisualIdentity", resolvedCapability.Workflow);
        Assert.Equal(1, resolvedCapability.WorkflowVersion);

        Assert.Equal("https://cdn.project00.ai/gen_fullbody.png", response.StandeeUrl);
    }

    [Fact]
    public async Task GenerateStandeeAsync_Prioritizes_Explicit_BodyReference_When_Provided()
    {
        var imageService = new RecordingImageGenerationService();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var httpClient = new System.Net.Http.HttpClient();
        var geminiClient = new Infrastructure.LLM.Core.GeminiApiClient(
            httpClient,
            config,
            NullLogger<Infrastructure.LLM.Core.GeminiApiClient>.Instance);

        var promptCompiler = new Infrastructure.LLM.Prompts.PromptCompiler();
        var llmService = new LLMService(
            geminiClient,
            imageService,
            promptCompiler);

        var standeeRequest = new GenerateStandeeRequest(
            name: "Lyra",
            title: "Dragon Sovereign",
            category: "Fantasy",
            avatarUrl: "https://cdn.project00.ai/gen_avatar.png",
            referenceImageUrl: "https://cdn.project00.ai/original.png",
            bodyReferenceUrl: "https://cdn.project00.ai/canonical_body_anchor.png"
        );

        var response = await llmService.GenerateStandeeAsync(standeeRequest, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(imageService.RecordedRequests);

        var standeeReq = imageService.RecordedRequests[0];
        Assert.Equal("https://cdn.project00.ai/canonical_body_anchor.png", standeeReq.ReferenceImageUrl);
    }

    [Fact]
    public async Task GenerateStandeeAsync_Falls_Back_To_Avatar_Only_When_No_Other_Reference_Exists()
    {
        var imageService = new RecordingImageGenerationService();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var httpClient = new System.Net.Http.HttpClient();
        var geminiClient = new Infrastructure.LLM.Core.GeminiApiClient(
            httpClient,
            config,
            NullLogger<Infrastructure.LLM.Core.GeminiApiClient>.Instance);

        var promptCompiler = new Infrastructure.LLM.Prompts.PromptCompiler();
        var llmService = new LLMService(
            geminiClient,
            imageService,
            promptCompiler);

        // Only avatar provided:
        var standeeRequest = new GenerateStandeeRequest(
            name: "Lyra",
            title: "Dragon Sovereign",
            category: "Fantasy",
            avatarUrl: "https://cdn.project00.ai/gen_avatar.png"
        );

        var response = await llmService.GenerateStandeeAsync(standeeRequest, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(imageService.RecordedRequests);

        var standeeReq = imageService.RecordedRequests[0];
        Assert.Equal("https://cdn.project00.ai/gen_avatar.png", standeeReq.ReferenceImageUrl);
    }

    [Fact]
    public void BuildStandeePrompt_Enforces_FullBody_Standing_Composition_And_Avoids_WaistUp()
    {
        var identity = new CharacterVisualIdentity(
            Gender: "Female",
            Hair: "Silver long hair",
            Eyes: "Blue",
            Body: "Slender, tall",
            ClothingStyle: "Royal tunic"
        );

        var prompt = Infrastructure.LLM.Prompts.CharacterGenerationPrompts.BuildStandeePrompt(
            name: "Aria",
            title: "Mage",
            category: "Fantasy",
            personality: "Wise",
            idea: "Magic scholar",
            worldGenre: Domain.Enums.WorldGenre.HighFantasy,
            visualIdentity: identity
        );

        Assert.Contains("full-body standing character", prompt);
        Assert.Contains("head-to-toe composition", prompt);
        Assert.Contains("feet fully visible", prompt);
        Assert.Contains("entire silhouette visible", prompt);
        Assert.DoesNotContain("waist-up", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Male", "1boy, solo")]
    [InlineData("Female", "1girl, solo")]
    [InlineData("Androgynous", "1person, solo")]
    [InlineData("Other", "1person, solo")]
    [InlineData(null, "1person, solo")]
    [InlineData("Unknown", "1person, solo")]
    public void BuildStandeePrompt_Resolves_Gender_Accurately_Without_Defaulting_Unknown_To_Female(string? genderInput, string expectedCoreTag)
    {
        var identity = new CharacterVisualIdentity(
            Gender: genderInput,
            Hair: "Black short hair"
        );

        var prompt = Infrastructure.LLM.Prompts.CharacterGenerationPrompts.BuildStandeePrompt(
            name: "Rowan",
            title: "Scout",
            category: "Adventure",
            personality: "Quiet",
            idea: "Wandering scout",
            worldGenre: Domain.Enums.WorldGenre.MundaneSliceOfLife,
            visualIdentity: identity
        );

        Assert.Contains(expectedCoreTag, prompt);
    }
}
