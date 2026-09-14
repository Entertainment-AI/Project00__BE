using Application.Abstractions.Responses;
using Application.DTOs;
using Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Characters.Commands.ExtractVisualIdentity;

public sealed class ExtractVisualIdentityHandler : IRequestHandler<ExtractVisualIdentityCommand, Result<ExtractVisualIdentityResponse>>
{
    private const int MaxImageBytes = 10 * 1024 * 1024; // 10 MB limit
    private readonly IStorageService _storageService;
    private readonly IVisualIdentityExtractor _extractor;
    private readonly ILogger<ExtractVisualIdentityHandler> _logger;

    public ExtractVisualIdentityHandler(
        IStorageService storageService,
        IVisualIdentityExtractor extractor,
        ILogger<ExtractVisualIdentityHandler> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<ExtractVisualIdentityResponse>> Handle(
        ExtractVisualIdentityCommand command,
        CancellationToken cancellationToken)
    {
        byte[] imageBytes;
        string contentType = command.ContentType ?? "image/jpeg";
        string referenceUrl;

        try
        {
            if (command.ImageStream != null)
            {
                using var memoryStream = new MemoryStream();
                await command.ImageStream.CopyToAsync(memoryStream, cancellationToken);
                imageBytes = memoryStream.ToArray();

                if (imageBytes.Length == 0)
                {
                    return Result<ExtractVisualIdentityResponse>.Failure(400, "Uploaded reference image is empty.");
                }

                if (imageBytes.Length > MaxImageBytes)
                {
                    return Result<ExtractVisualIdentityResponse>.Failure(400, "Reference image exceeds maximum allowed size of 10 MB.");
                }

                var safeFileName = command.FileName ?? $"{Guid.NewGuid():N}.jpg";
                referenceUrl = await _storageService.SaveImageAsync(
                    imageBytes,
                    safeFileName,
                    contentType,
                    cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(command.ExistingImageUrl))
            {
                var inputUrl = command.ExistingImageUrl.Trim();

                if (inputUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIdx = inputUrl.IndexOf(',');
                    if (commaIdx == -1)
                    {
                        return Result<ExtractVisualIdentityResponse>.Failure(400, "Malformed base64 data image URL.");
                    }

                    var header = inputUrl[..commaIdx];
                    if (header.Contains("png", StringComparison.OrdinalIgnoreCase)) contentType = "image/png";
                    else if (header.Contains("webp", StringComparison.OrdinalIgnoreCase)) contentType = "image/webp";
                    else contentType = "image/jpeg";

                    var base64Part = inputUrl[(commaIdx + 1)..];
                    imageBytes = Convert.FromBase64String(base64Part);

                    if (imageBytes.Length > MaxImageBytes)
                    {
                        return Result<ExtractVisualIdentityResponse>.Failure(400, "Base64 reference image exceeds maximum allowed size of 10 MB.");
                    }

                    var ext = contentType == "image/png" ? ".png" : (contentType == "image/webp" ? ".webp" : ".jpg");
                    referenceUrl = await _storageService.SaveImageAsync(
                        imageBytes,
                        $"{Guid.NewGuid():N}{ext}",
                        contentType,
                        cancellationToken);
                }
                else
                {
                    referenceUrl = inputUrl;
                    // If local file in wwwroot, read from disk
                    string? localPath = null;
                    if (inputUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) || inputUrl.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
                    {
                        var cleanRel = inputUrl.TrimStart('/');
                        var possible1 = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", cleanRel.Replace('/', Path.DirectorySeparatorChar));
                        var possible2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", cleanRel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(possible1)) localPath = possible1;
                        else if (File.Exists(possible2)) localPath = possible2;
                    }

                    if (localPath != null && File.Exists(localPath))
                    {
                        imageBytes = await File.ReadAllBytesAsync(localPath, cancellationToken);
                        if (localPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) contentType = "image/png";
                        else if (localPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) contentType = "image/webp";
                    }
                    else
                    {
                        return Result<ExtractVisualIdentityResponse>.Failure(400, "Reference image must be uploaded via stream or valid data URL for identity extraction.");
                    }
                }
            }
            else
            {
                return Result<ExtractVisualIdentityResponse>.Failure(400, "No reference image was provided.");
            }

            // Extract structured, zero-hallucination semantic visual identity
            var extractedResult = await _extractor.ExtractIdentityAsync(imageBytes, contentType, cancellationToken);

            _logger.LogInformation("Successfully extracted visual identity for reference image: {ReferenceUrl}", referenceUrl);

            return Result<ExtractVisualIdentityResponse>.Success(new ExtractVisualIdentityResponse(referenceUrl, extractedResult));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to extract visual identity from reference image");
            return Result<ExtractVisualIdentityResponse>.Failure(500, $"Failed to process reference image: {ex.Message}");
        }
    }
}
