using Meridian.Api.Shared.Errors;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Meridian.Api.Features.Imports;

/// <summary>
/// Raises the body and multipart limits for one endpoint. Does what [RequestSizeLimit] and
/// [RequestFormLimits] do, which only take constants, so the limit can come from configuration.
/// </summary>
public class UploadLimitFilter(IOptions<UploadOptions> options) : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        long maxBytes = options.Value.MaxBytes;
        HttpContext http = context.HttpContext;

        bool tooLarge = http.Request.ContentLength > maxBytes;

        // Kestrel reads an unread body after the response, but only within its limit. Past that it
        // resets the connection while the client is still sending, and the client never sees the 413
        IHttpMaxRequestBodySizeFeature? bodySize = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
        {
            bodySize.MaxRequestBodySize = tooLarge ? null : maxBytes;
        }

        if (tooLarge)
        {
            throw new FileTooLargeException(maxBytes);
        }

        http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions { MultipartBodyLengthLimit = maxBytes }));
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
