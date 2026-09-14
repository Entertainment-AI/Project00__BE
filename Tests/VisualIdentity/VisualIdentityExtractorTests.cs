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
            CanonicalReferenceUrl: "https://cdn.project00.ai/face_ref.png",
            FullBodyUrl: "https://cdn.project00.ai/fullbody_ref.png",
            SignatureFeatures: new List<string> { "curved black and red dragon horns", "pointy ears" },
            Style: "Anime"
        );

        var identity = confirmed.ToDomainEntity();

        Assert.Equal("Female", identity.Gender);
        Assert.Equal("vivid crimson red almond eyes", identity.Eyes);
        Assert.Equal("waist-length silver-white straight hair", identity.Hair);
        Assert.Equal("pale porcelain", identity.Skin);
        Assert.Equal("slender build, long-legged appearance", identity.Body);
        Assert.Equal("https://cdn.project00.ai/original_upload.png", identity.OriginalReferenceUrl);
        Assert.Equal("https://cdn.project00.ai/face_ref.png", identity.CanonicalReferenceUrl);
        Assert.Equal("https://cdn.project00.ai/fullbody_ref.png", identity.FullBodyUrl);
        Assert.Equal(2, identity.SignatureFeatures?.Count);
        Assert.Equal("curved black and red dragon horns", identity.SignatureFeatures?[0].Name);
        Assert.Equal(FeaturePersistence.EveryTurn, identity.SignatureFeatures?[0].Persistence);
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

    [Fact]
    public async Task GenerateAvatarAsync_With_ReferenceImageUrl_Conditions_Only_Avatar_As_Independent_Job()
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
        // EXACTLY 1 generation request for Avatar (decoupled from Standee!)
        Assert.Single(imageService.RecordedRequests);

        var avatarReq = imageService.RecordedRequests[0];
        Assert.Equal("VisualIdentity", avatarReq.Workflow);
        Assert.Equal(1, avatarReq.WorkflowVersion);
        Assert.Equal("https://cdn.project00.ai/user_uploaded_reference.png", avatarReq.ReferenceImageUrl);
        Assert.Equal(512, avatarReq.Width);
        Assert.Equal(512, avatarReq.Height);
        Assert.Contains("\"weight\":0.65", avatarReq.ParametersJson!);

        Assert.Equal("https://cdn.project00.ai/gen_avatar.png", response.AvatarUrl);
        Assert.Null(response.FullBodyUrl); // Decoupled!
    }

    [Fact]
    public async Task GenerateStandeeAsync_Conditions_Standee_Independently()
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
            referenceImageUrl: "https://cdn.project00.ai/user_uploaded_reference.png"
        );

        var response = await llmService.GenerateStandeeAsync(standeeRequest, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(imageService.RecordedRequests);

        var standeeReq = imageService.RecordedRequests[0];
        Assert.Equal("VisualIdentity", standeeReq.Workflow);
        Assert.Equal(1, standeeReq.WorkflowVersion);
        Assert.Equal("https://cdn.project00.ai/gen_avatar.png", standeeReq.ReferenceImageUrl);
        Assert.Equal(512, standeeReq.Width);
        Assert.Equal(768, standeeReq.Height);
        Assert.Contains("\"weight\":0.45", standeeReq.ParametersJson!);

        Assert.Equal("https://cdn.project00.ai/gen_fullbody.png", response.StandeeUrl);
    }
}
