using Application.Abstractions.Responses;
using Application.DTOs;
using Application.Interfaces;
using MediatR;

namespace Application.Features.Characters.Commands.GenerateStandee;

public sealed class GenerateStandeeHandler : IRequestHandler<GenerateStandeeCommand, Result<GenerateStandeeResponse>>
{
    private readonly ILLMService _llmService;

    public GenerateStandeeHandler(ILLMService llmService)
    {
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
    }

    public async Task<Result<GenerateStandeeResponse>> Handle(GenerateStandeeCommand command, CancellationToken cancellationToken)
    {
        var response = await _llmService.GenerateStandeeAsync(command.Request, cancellationToken);
        return Result<GenerateStandeeResponse>.Success(response);
    }
}
