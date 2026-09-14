using Application.Common;
using Application.DTOs;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Infrastructure.ImageGeneration;
using Infrastructure.ImageGeneration.ComfyUI;
using Infrastructure.LLM.Prompts;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests.GenerationProduction;

public sealed class StyleModelSelectionTests
{
    private static IConfiguration CreateDefaultStyleConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiProviders:ImageGeneration:StyleModels:Anime"] = "meinamix",
                ["AiProviders:ImageGeneration:StyleModels:Manhwa"] = "meinamix",
                ["AiProviders:ImageGeneration:StyleModels:Realistic"] = "majicmixrealistic",
                ["AiProviders:ImageGeneration:StyleModels:Cinematic"] = "majicmixrealistic"
            })
            .Build();

    private static Character CreateCharacterWithStyle(VisualStyle style)
    {
        var identity = new CharacterVisualIdentity(
            Style: style.ToString(),
            VisualStyle: style,
            CanonicalReferenceUrl: "https://cdn.project00.ai/aria_face.png"
        );

        return new Character(
            name: $"Aria_{style}",
            title: "Protagonist",
            avatarUrl: "https://cdn.project00.ai/avatar.png",
            personalityPrompt: "Determined warrior",
            greeting: "Greetings",
            category: "General",
            visualIdentity: identity
        );
    }

    [Fact]
    public void Test1_AnimeCharacter_ResolvesToMeinamix()
    {
        var config = CreateDefaultStyleConfiguration();
        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(configuration: config, modelRegistry: registry);
        var character = CreateCharacterWithStyle(VisualStyle.Anime);

        var profile = provider.ResolveProfile(character);

        Assert.Equal("meinamix", profile.Model);
    }

    [Fact]
    public void Test2_RealisticCharacter_ResolvesToMajicMixRealistic()
    {
        var config = CreateDefaultStyleConfiguration();
        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(configuration: config, modelRegistry: registry);
        var character = CreateCharacterWithStyle(VisualStyle.Realistic);

        var profile = provider.ResolveProfile(character);

        Assert.Equal("majicmixrealistic", profile.Model);
    }

    [Fact]
    public void Test3_CinematicCharacter_ResolvesToMajicMixRealistic()
    {
        var config = CreateDefaultStyleConfiguration();
        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(configuration: config, modelRegistry: registry);
        var character = CreateCharacterWithStyle(VisualStyle.Cinematic);

        var profile = provider.ResolveProfile(character);

        Assert.Equal("majicmixrealistic", profile.Model);
    }

    [Fact]
    public void Test4_ManhwaCharacter_ResolvesToMeinamix()
    {
        var config = CreateDefaultStyleConfiguration();
        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(configuration: config, modelRegistry: registry);
        var character = CreateCharacterWithStyle(VisualStyle.Manhwa);

        var profile = provider.ResolveProfile(character);

        Assert.Equal("meinamix", profile.Model);
    }

    [Fact]
    public void Test5_CharacterWithUnspecifiedStyle_ThrowsInvalidOperationException()
    {
        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(modelRegistry: registry);
        var character = CreateCharacterWithStyle(VisualStyle.Unspecified);

        var ex = Assert.Throws<InvalidOperationException>(() => provider.ResolveProfile(character));
        Assert.Contains("Aria_Unspecified", ex.Message);
        Assert.Contains("does not have an explicit VisualStyle selected", ex.Message);
    }

    [Fact]
    public void Test6_Configuration_OverridesStyleModelMapping()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiProviders:ImageGeneration:StyleModels:Realistic"] = "custom-realistic-v2",
                ["AiProviders:ImageGeneration:StyleModels:Anime"] = "custom-anime-v3"
            })
            .Build();

        var provider = new VisualGenerationProfileProvider(configuration: config);

        var realisticChar = CreateCharacterWithStyle(VisualStyle.Realistic);
        var animeChar = CreateCharacterWithStyle(VisualStyle.Anime);

        Assert.Equal("custom-realistic-v2", provider.ResolveProfile(realisticChar).Model);
        Assert.Equal("custom-anime-v3", provider.ResolveProfile(animeChar).Model);
    }

    [Fact]
    public void Test7_EndToEnd_AnimeAndRealisticCharacters_PropagateThroughToComfyUIWorkflowCheckpoints()
    {
        var config = CreateDefaultStyleConfiguration();
        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(configuration: config, modelRegistry: registry);
        var policy = WorkflowCapabilityPolicy.CreateDefault(registry);
        var builder = new VisualIdentityWorkflowV1Builder(registry);

        // 1. Anime character path
        var animeChar = CreateCharacterWithStyle(VisualStyle.Anime);
        var animeProfile = provider.ResolveProfile(animeChar);
        Assert.Equal("meinamix", animeProfile.Model);

        var animeSnapshot = new VisualSnapshot(
            TurnId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            CharacterId: animeChar.Id,
            SceneRevision: 1,
            VisualIdentity: animeChar.VisualIdentity,
            SceneState: new SessionSceneState("City", "Center"),
            TransientState: null,
            GenerationProfile: animeProfile
        );

        var animeRequest = ImageGenerationRequest.FromSnapshot(animeSnapshot, "1girl in futuristic neon city");
        Assert.Equal("meinamix", animeRequest.Model);

        // Verify capability policy validation passes
        Assert.True(policy.IsSupported(animeRequest.ResolveEffectiveCapability()));

        // Verify ComfyUI node graph populates the anime checkpoint artifact
        var animeGraph = builder.BuildWorkflow(animeRequest, "face.png");
        var animeNode4 = (Dictionary<string, object>)animeGraph["4"];
        var animeInputs = (Dictionary<string, object>)animeNode4["inputs"];
        Assert.Equal("meinamix_meinaV11.safetensors", animeInputs["ckpt_name"]);

        // 2. Realistic character path (defaults to majicmixrealistic in PR78)
        var realisticChar = CreateCharacterWithStyle(VisualStyle.Realistic);
        var realisticProfile = provider.ResolveProfile(realisticChar);
        Assert.Equal("majicmixrealistic", realisticProfile.Model);

        var realisticSnapshot = new VisualSnapshot(
            TurnId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            CharacterId: realisticChar.Id,
            SceneRevision: 1,
            VisualIdentity: realisticChar.VisualIdentity,
            SceneState: new SessionSceneState("Park", "Bench"),
            TransientState: null,
            GenerationProfile: realisticProfile
        );

        var realisticRequest = ImageGenerationRequest.FromSnapshot(realisticSnapshot, "1man sitting on park bench photorealistic");
        Assert.Equal("majicmixrealistic", realisticRequest.Model);

        // Verify capability policy validation passes
        Assert.True(policy.IsSupported(realisticRequest.ResolveEffectiveCapability()));

        // Verify ComfyUI node graph populates the majicmixrealistic checkpoint artifact
        var realisticGraph = builder.BuildWorkflow(realisticRequest, "face.png");
        var realisticNode4 = (Dictionary<string, object>)realisticGraph["4"];
        var realisticInputs = (Dictionary<string, object>)realisticNode4["inputs"];
        Assert.Equal("majicmixRealistic_v7.safetensors", realisticInputs["ckpt_name"]);
    }

    [Fact]
    public void Test7b_EpicRealism_RemainsBackwardCompatible_WhenExplicitlyConfigured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiProviders:ImageGeneration:StyleModels:Realistic"] = "epicrealism"
            })
            .Build();
        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(configuration: config, modelRegistry: registry);
        var builder = new VisualIdentityWorkflowV1Builder(registry);

        var character = CreateCharacterWithStyle(VisualStyle.Realistic);
        var profile = provider.ResolveProfile(character);
        Assert.Equal("epicrealism", profile.Model);

        var snapshot = new VisualSnapshot(
            TurnId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            CharacterId: character.Id,
            SceneRevision: 1,
            VisualIdentity: character.VisualIdentity,
            SceneState: new SessionSceneState("Park", "Bench"),
            TransientState: null,
            GenerationProfile: profile
        );

        var request = ImageGenerationRequest.FromSnapshot(snapshot, "1man sitting on park bench");
        var graph = builder.BuildWorkflow(request, "face.png");
        var node4 = (Dictionary<string, object>)graph["4"];
        var inputs = (Dictionary<string, object>)node4["inputs"];
        Assert.Equal("epicrealism_naturalSinRC1VAE.safetensors", inputs["ckpt_name"]);
    }

    [Fact]
    public void Test8_NormalizeModelId_WithRegistry_WhenUnknownModelConfigured_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiProviders:ImageGeneration:StyleModels:Realistic"] = "unknown-nonexistent-model"
            })
            .Build();

        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(configuration: config, modelRegistry: registry);
        var realisticChar = CreateCharacterWithStyle(VisualStyle.Realistic);

        var ex = Assert.Throws<InvalidOperationException>(() => provider.ResolveProfile(realisticChar));
        Assert.Contains("unknown-nonexistent-model", ex.Message);
        Assert.Contains("not registered in ModelRegistry", ex.Message);
    }

    [Fact]
    public void Test9_CategoryDoesNotInferVisualStyle_WhenVisualIdentityStyleUnspecified_ThrowsInvalidOperationException()
    {
        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(modelRegistry: registry);

        // Character has category "Anime", but VisualIdentity has VisualStyle.Unspecified
        var identity = new CharacterVisualIdentity(
            Hair: "Silver",
            Eyes: "Crimson",
            CanonicalReferenceUrl: "https://cdn.project00.ai/aria_face.png",
            VisualStyle: VisualStyle.Unspecified
        );

        var character = new Character(
            name: "Aria_CategoryAnimeOnly",
            title: "Protagonist",
            avatarUrl: "https://cdn.project00.ai/avatar.png",
            personalityPrompt: "Determined warrior",
            greeting: "Greetings",
            category: "Anime",
            visualIdentity: identity
        );

        var ex = Assert.Throws<InvalidOperationException>(() => provider.ResolveProfile(character));
        Assert.Contains("Aria_CategoryAnimeOnly", ex.Message);
        Assert.Contains("does not have an explicit VisualStyle selected", ex.Message);
    }

    [Fact]
    public void Test10_UnconfiguredStyle_WithoutDefaultModel_ThrowsInvalidOperationException()
    {
        var provider = new VisualGenerationProfileProvider();
        var character = CreateCharacterWithStyle(VisualStyle.PixelArt);

        var ex = Assert.Throws<InvalidOperationException>(() => provider.ResolveProfile(character));
        Assert.Contains("No image generation model is mapped or configured for VisualStyle 'PixelArt'", ex.Message);
        Assert.Contains("AiProviders:ImageGeneration:StyleModels:PixelArt", ex.Message);
    }

    [Fact]
    public void Test11_UnconfiguredStyle_FallsBackToGlobalDefaultModel()
    {
        // Global default model configured, but specific style has no StyleModels entry
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiProviders:ImageGeneration:DefaultModel"] = "global-fallback-model"
            })
            .Build();

        var provider = new VisualGenerationProfileProvider(configuration: config);
        var character = CreateCharacterWithStyle(VisualStyle.PixelArt);

        var profile = provider.ResolveProfile(character);
        Assert.Equal("global-fallback-model", profile.Model);
    }

    [Fact]
    public async Task Test12_GenerateAvatar_DelegatesModelResolutionDownstream()
    {
        var capturedRequests = new List<ImageGenerationRequest>();
        var capturingImageService = new CapturingImageService(capturedRequests);
        var promptCompiler = new Infrastructure.LLM.Prompts.PromptCompiler();
        var geminiConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:ApiKey"] = "dummy" })
            .Build();
        var gemini = new Infrastructure.LLM.Core.GeminiApiClient(
            new HttpClient(),
            geminiConfig,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Infrastructure.LLM.Core.GeminiApiClient>.Instance);

        var llm = new Infrastructure.LLM.LLMService(
            geminiClient: gemini,
            imageService: capturingImageService,
            promptCompiler: promptCompiler
        );

        var request = new GenerateAvatarRequest(
            name: "Lâm Uyển Nhi",
            title: "Họa Sĩ",
            personalityPrompt: "Trầm tính",
            visualIdentity: new CharacterVisualIdentity(
                Style: "Realistic",
                VisualStyle: VisualStyle.Realistic
            )
        );

        await llm.GenerateAvatarAsync(request);

        Assert.NotEmpty(capturedRequests);
        // LLMService does not pollute requests with hardcoded model selection; delegates downstream
        Assert.All(capturedRequests, req => Assert.Null(req.Model));
    }

    [Fact]
    public void Test13_MajicMixRealistic_RegisteredInBaseline_AndResolvesArtifact()
    {
        var registry = new ConfigurationModelRegistry();

        var modelDef = registry.FindById("majicmixrealistic");
        Assert.NotNull(modelDef);
        Assert.Equal(ModelFamily.Sd15, modelDef.Family);
        Assert.Equal("majicmixRealistic_v7.safetensors", modelDef.ArtifactName);

        // Alias resolution via FindById
        var byArtifact = registry.FindById("majicmixRealistic_v7.safetensors");
        Assert.NotNull(byArtifact);
        Assert.Equal("majicmixrealistic", byArtifact.Id);

        var byCanonicalAlias = registry.FindById("majicmixrealistic.safetensors");
        Assert.NotNull(byCanonicalAlias);
        Assert.Equal("majicmixrealistic", byCanonicalAlias.Id);
    }

    [Fact]
    public void Test14_MajicMixRealistic_PropagatesThroughProfileProviderToComfyUIWorkflow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiProviders:ImageGeneration:StyleModels:Realistic"] = "majicmixrealistic",
                ["AiProviders:ImageGeneration:StyleModels:Cinematic"] = "majicmixrealistic"
            })
            .Build();
        var registry = new ConfigurationModelRegistry();
        var provider = new VisualGenerationProfileProvider(configuration: config, modelRegistry: registry);
        var builder = new VisualIdentityWorkflowV1Builder(registry);

        var character = CreateCharacterWithStyle(VisualStyle.Realistic);
        var profile = provider.ResolveProfile(character);
        Assert.Equal("majicmixrealistic", profile.Model);

        var snapshot = new VisualSnapshot(
            TurnId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            CharacterId: character.Id,
            SceneRevision: 1,
            VisualIdentity: character.VisualIdentity,
            SceneState: new SessionSceneState("Garden", "Bench"),
            TransientState: null,
            GenerationProfile: profile
        );

        var request = ImageGenerationRequest.FromSnapshot(snapshot, "1girl sitting in sunlit garden");
        Assert.Equal("majicmixrealistic", request.Model);

        var graph = builder.BuildWorkflow(request, "face.png");
        var node4 = (Dictionary<string, object>)graph["4"];
        var inputs = (Dictionary<string, object>)node4["inputs"];
        Assert.Equal("majicmixRealistic_v7.safetensors", inputs["ckpt_name"]);
    }

    private sealed class CapturingImageService : IImageGenerationService
    {
        private readonly List<ImageGenerationRequest> _captured;
        public CapturingImageService(List<ImageGenerationRequest> captured) => _captured = captured;

        public Task<string> GenerateImageAsync(string prompt, int width = 512, int height = 512, CancellationToken ct = default)
            => Task.FromResult("https://storage.local/dummy.png");

        public Task<string> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
        {
            _captured.Add(request);
            return Task.FromResult("https://storage.local/dummy.png");
        }

        public Task<ImageGenerationResult> GenerateImageWithResultAsync(ImageGenerationRequest request, CancellationToken ct = default)
        {
            _captured.Add(request);
            return Task.FromResult(new ImageGenerationResult("https://storage.local/dummy.png", "ComfyUI", "job-1", 100, 12345));
        }
    }
}
