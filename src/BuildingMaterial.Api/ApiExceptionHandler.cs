using BuildingMaterial.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BuildingMaterial.Api;

internal sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger,
    SlackErrorNotifier slackErrorNotifier) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Catat detail error ke logger dan kirim notifikasi ke Slack
        logger.LogError(exception, "Unhandled exception processing {Method} {Path}.",
            httpContext.Request.Method, httpContext.Request.Path);
        await slackErrorNotifier.NotifyAsync(exception, httpContext.Request.Path, cancellationToken);

        // Kembalikan respons HTTP 500 dengan format ProblemDetails
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "The request could not be completed.",
            Instance = httpContext.Request.Path
        }, cancellationToken);

        // Kembalikan true untuk menandakan exception sudah ditangani
        return true;
    }
}
