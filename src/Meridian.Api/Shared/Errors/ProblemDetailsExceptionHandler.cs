using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Api.Shared.Errors;

/// <summary>Maps app exceptions to RFC 9457 responses. Anything else is left to the framework as a bare 500.</summary>
public class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException validation => ToProblem(validation),
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = notFound.Message,
            },
            ConcurrencyConflictException conflict => ToProblem(conflict),
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new()
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static HttpValidationProblemDetails ToProblem(ValidationException exception)
    {
        Dictionary<string, string[]> errors = exception.Errors
            .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return new HttpValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest };
    }

    private static ProblemDetails ToProblem(ConcurrencyConflictException exception)
    {
        ProblemDetails problem = new()
        {
            Status = StatusCodes.Status409Conflict,
            Type = exception.ProblemType,
            Title = "Conflict",
            Detail = exception.Message,
        };

        if (exception.Current is not null)
        {
            problem.Extensions["current"] = exception.Current;
        }

        return problem;
    }
}
