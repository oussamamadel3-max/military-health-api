using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace API.Infrastructure;

public static class ApiResponse
{
    // 🟢 Success Response (Endpoint IResult)
    public static IResult Success(string message, object? data = null, object? meta = null, int status = 200)
    {
        return Results.Json(new
        {
            success = true,
            message = message,
            data = data ?? new { },
            meta = meta ?? new { }
        }, statusCode: status);
    }

    // 🔴 Error Response (Endpoint IResult)
    public static IResult Error(string message, string code, object? meta = null, int status = 400)
    {
        return Results.Json(new
        {
            success = false,
            error = new
            {
                code = code,
                message = message,
                meta = meta ?? new { }
            }
        }, statusCode: status);
    }

    // 🔴 Error Response (Filter ObjectResult)
    public static ObjectResult ErrorResult(string message, string code, object? meta = null, int status = 400)
    {
        return new ObjectResult(new
        {
            success = false,
            error = new
            {
                code = code,
                message = message,
                meta = meta ?? new { }
            }
        })
        { StatusCode = status };
    }
}
