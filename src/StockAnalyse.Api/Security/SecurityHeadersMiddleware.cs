namespace StockAnalyse.Api.Security;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "same-origin";

        if (!IsSafeMethod(context.Request.Method)
            && !HasSameOriginWhenProvided(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers["Cache-Control"] = "no-store";
            return;
        }

        await _next(context);
    }

    private static bool IsSafeMethod(string method)
    {
        return HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method);
    }

    private static bool HasSameOriginWhenProvided(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var originValues))
        {
            return true;
        }

        return Uri.TryCreate(originValues.ToString(), UriKind.Absolute, out var origin)
            && origin.Authority.Equals(request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
