namespace Domain.Exceptions;

public abstract class BusinessException : Exception
{
    public string ErrorCode { get; }
    public int Status { get; }
    public object? Meta { get; }

    protected BusinessException(
        string message,
        string errorCode,
        int status = 400,
        object? meta = null)
        : base(message)
    {
        ErrorCode = errorCode;
        Status = status;
        Meta = meta;
    }
}
