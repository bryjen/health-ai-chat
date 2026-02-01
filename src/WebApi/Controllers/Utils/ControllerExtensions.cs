using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;

namespace WebApi.Controllers.Utils;

/// <summary>
/// Extension methods for controllers to return standardized error responses
/// </summary>
public static class ControllerExtensions
{
    /// <summary>
    /// Returns a standardized 400 Bad Request error response
    /// </summary>
    public static ActionResult BadRequestError(this ControllerBase controller, string message) 
        => controller.BadRequest(new ErrorResponse { Message = message });

    /// <summary>
    /// Returns a standardized 404 Not Found error response
    /// </summary>
    public static ActionResult NotFoundError(this ControllerBase controller, string message) 
        => controller.NotFound(new ErrorResponse { Message = message });

    /// <summary>
    /// Returns a standardized 409 Conflict error response
    /// </summary>
    public static ActionResult ConflictError(this ControllerBase controller, string message) 
        => controller.Conflict(new ErrorResponse { Message = message });

    /// <summary>
    /// Returns a standardized 401 Unauthorized error response
    /// </summary>
    public static ActionResult UnauthorizedError(this ControllerBase controller, string message) 
        => controller.Unauthorized(new ErrorResponse { Message = message });
}
