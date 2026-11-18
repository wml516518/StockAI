using System.Text;

namespace StockAnalyse.Api.Security;

public sealed class BasicAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BasicAuthSettings _settings;

    public BasicAuthMiddleware(RequestDelegate next, BasicAuthSettings settings)
    {
        _next = next;
        _settings = settings;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "same-origin";

        if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!TryReadCredentials(context.Request, out var username, out var password)
            || !_settings.IsValid(username, password))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["WWW-Authenticate"] =
                "Basic realm=\"StockAI\", charset=\"UTF-8\"";
            context.Response.Headers["Cache-Control"] = "no-store";
            return;
        }

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

    private static bool TryReadCredentials(HttpRequest request, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var encoded = authorization["Basic ".Length..].Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var separator = decoded.IndexOf(':');
            if (separator < 0)
            {
                return false;
            }

            username = decoded[..separator];
            password = decoded[(separator + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
