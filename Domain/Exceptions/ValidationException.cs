namespace Domain.Exceptions;

public class ValidationException : BusinessException
{
    public ValidationException(object errors, string message = "Validation error")
        : base(
            message: message,
            errorCode: "VALIDATION_ERROR",
            status: 422,
            meta: new { errors })
    {
    }
}
