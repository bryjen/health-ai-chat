using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Web.Common.DTOs;
using WebApi.Exceptions;

namespace WebApi.Middleware;

/// <summary>
/// Last resort global exception handling middleware intended for uncaught errors that propagate through user code.
/// </summary>
public class GlobalExceptionHandlerMiddleware(
    RequestDelegate next, 
    ILogger<GlobalExceptionHandlerMiddleware> logger,
    ICorsService corsService,
    ICorsPolicyProvider corsPolicyProvider)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            // in the case of an uncaught "domain" exception (user-defined error that could have been but wasn't handled)
            // add an extra warning log
            // indicates that it should be handled by the controller
            if (ex is ValidationException or NotFoundException or ConflictException or UnauthorizedAccessException)
            {
                logger.LogWarning(
                    "Domain exception {ExceptionType} reached middleware. Controllers should handle this: {Message}",
                    ex.GetType().Name,
                    ex.Message);
                await HandleDomainExceptionAsync(context, ex);
                return;
            }

            logger.LogError(ex, "An unhandled exception occurred: {ExceptionType}", ex.GetType().Name);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleDomainExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        
        if (response.HasStarted)
        {
            logger.LogWarning("Cannot write error response - response has already started");
            return;
        }

        response.Clear();

        // apply CORS headers before writing the response (if CORS is enabled)
        if (corsPolicyProvider != null && corsService != null)
        {
            var policy = await corsPolicyProvider.GetPolicyAsync(context, null);
            if (policy != null)
            {
                var corsResult = corsService.EvaluatePolicy(context, policy);
                corsService.ApplyResult(corsResult, response);
            }
        }

        response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            Message = exception.Message
        };

        response.StatusCode = exception switch
        {
            ValidationException => (int)HttpStatusCode.BadRequest,
            NotFoundException => (int)HttpStatusCode.NotFound,
            ConflictException => (int)HttpStatusCode.Conflict,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            _ => (int)HttpStatusCode.InternalServerError
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        try
        {
            await response.WriteAsync(jsonResponse);
            await response.Body.FlushAsync();
        }
        catch (Exception writeEx)
        {
            logger.LogError(writeEx, "Failed to write error response to client");
            if (!response.HasStarted)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        
        if (response.HasStarted)
        {
            logger.LogWarning("Cannot write error response - response has already started");
            return;
        }

        response.Clear();

        // apply CORS headers before writing the response (if CORS is enabled)
        if (corsPolicyProvider != null && corsService != null)
        {
            var policy = await corsPolicyProvider.GetPolicyAsync(context, null);
            if (policy != null)
            {
                var corsResult = corsService.EvaluatePolicy(context, policy);
                corsService.ApplyResult(corsResult, response);
            }
        }

        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var errorResponse = new ErrorResponse
        {
            Message = "An error occurred while processing your request."
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        try
        {
            await response.WriteAsync(jsonResponse);
            await response.Body.FlushAsync();
        }
        catch (Exception writeEx)
        {
            logger.LogError(writeEx, "Failed to write error response to client");
            // if we're unable to write, atl try to set the status code if it hasn't been set yet
            if (!response.HasStarted)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
        }
    }
}
