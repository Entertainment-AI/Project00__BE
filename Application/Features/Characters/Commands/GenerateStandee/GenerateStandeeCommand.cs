using Application.Abstractions.Responses;
using Application.DTOs;
using MediatR;

namespace Application.Features.Characters.Commands.GenerateStandee;

public sealed record GenerateStandeeCommand(GenerateStandeeRequest Request) : IRequest<Result<GenerateStandeeResponse>>;
