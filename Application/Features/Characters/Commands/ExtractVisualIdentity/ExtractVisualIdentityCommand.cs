using Application.Abstractions.Responses;
using Application.DTOs;
using MediatR;

namespace Application.Features.Characters.Commands.ExtractVisualIdentity;

public sealed record ExtractVisualIdentityCommand(
    Stream? ImageStream = null,
    string? FileName = null,
    string? ContentType = null,
    string? ExistingImageUrl = null
) : IRequest<Result<ExtractVisualIdentityResponse>>;
