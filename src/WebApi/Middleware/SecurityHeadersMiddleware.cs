namespace WebApi.Middleware;

/// <summary>
/// Middleware to add security headers to all HTTP responses
/// </summary>
public class SecurityHeadersMiddleware(
    RequestDelegate next, 
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";  // prevents MIME type sniffing
        headers.XFrameOptions = "DENY";  // prevents clickjacking
        headers.XXSSProtection = "1; mode=block";  // legacy XSS protection (for older browsers)
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=(), payment=(), usb=()";  // permissions policy - restrict browser features

        if (environment.IsProduction())
            headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";  // HSTS, assumes production with https

        // Content Security Policy - less critical for APIs, but still useful
        if (environment.IsProduction())
            headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

        await next(context);
    }
}
