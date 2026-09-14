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
    public void ToCharacterVisualIdentity_Maps_All_Extracted_Fields_Faithfully()
    {
        var extracted = new VisualIdentityExtractionResult(
            Face: new ExtractedFace("oval", "vivid crimson red almond eyes", "soft delicate chin"),
            Hair: new ExtractedHair("silver-white", "straight with side bangs", "waist-length"),
            Skin: new ExtractedSkin("pale porcelain"),
            Body: new ExtractedBody("slender build", "long-legged with narrow waist", "graceful silhouette"),
            SignatureFeatures: new List<string> { "curved black and red dragon horns", "pointy ears" },
            VisualTraits: "dark fantasy warrior aesthetic",
            ObservableGender: "Female"
        );

        var identity = extracted.ToCharacterVisualIdentity(
            canonicalReferenceUrl: "https://cdn.project00.ai/face_ref.png",
            fullBodyUrl: "https://cdn.project00.ai/fullbody_ref.png",
            style: "Anime"
        );

        Assert.Equal("Female", identity.Gender);
        Assert.Equal("vivid crimson red almond eyes", identity.Eyes);
        Assert.Equal("waist-length silver-white straight with side bangs hair", identity.Hair);
        Assert.Equal("pale porcelain", identity.Skin);
        Assert.Contains("slender build", identity.Body);
        Assert.Contains("long-legged with narrow waist", identity.Body);
        Assert.Contains("graceful silhouette", identity.Body);
        Assert.Equal("https://cdn.project00.ai/face_ref.png", identity.CanonicalReferenceUrl);
        Assert.Equal("https://cdn.project00.ai/fullbody_ref.png", identity.FullBodyUrl);
        Assert.Equal(2, identity.SignatureFeatures?.Count);
        Assert.Equal("curved black and red dragon horns", identity.SignatureFeatures?[0].Name);
        Assert.Equal(FeaturePersistence.EveryTurn, identity.SignatureFeatures?[0].Persistence);
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
    public async Task GenerateAvatarAsync_With_ReferenceImageUrl_Conditions_Both_Avatar_And_FullBody_On_VisualIdentity_Workflow()
    {
        var imageService = new RecordingImageGenerationService();

        // Create LLMService with stub image service
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
        Assert.Equal(2, imageService.RecordedRequests.Count);

        // Avatar request MUST use VisualIdentity conditioned on ReferenceImageUrl
        var avatarReq = imageService.RecordedRequests[0];
        Assert.Equal("VisualIdentity", avatarReq.Workflow);
        Assert.Equal(1, avatarReq.WorkflowVersion);
        Assert.Equal("https://cdn.project00.ai/user_uploaded_reference.png", avatarReq.ReferenceImageUrl);
        Assert.Equal(512, avatarReq.Width);
        Assert.Equal(512, avatarReq.Height);
        Assert.Contains("\"weight\":0.65", avatarReq.ParametersJson!);

        // FullBody request MUST use VisualIdentity conditioned on Avatar and with height 768
        var fullBodyReq = imageService.RecordedRequests[1];
        Assert.Equal("VisualIdentity", fullBodyReq.Workflow);
        Assert.Equal(1, fullBodyReq.WorkflowVersion);
        Assert.Equal("https://cdn.project00.ai/gen_avatar.png", fullBodyReq.ReferenceImageUrl);
        Assert.Equal(512, fullBodyReq.Width);
        Assert.Equal(768, fullBodyReq.Height);
        Assert.Contains("\"weight\":0.45", fullBodyReq.ParametersJson!);

        // Output URLs
        Assert.Equal("https://cdn.project00.ai/gen_avatar.png", response.AvatarUrl);
        Assert.Equal("https://cdn.project00.ai/gen_fullbody.png", response.FullBodyUrl);
    }
}
