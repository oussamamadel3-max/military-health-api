using Domain.Exceptions;

namespace Application.Exceptions;

public class InvalidFileException : BusinessException
{
    public InvalidFileException(string message = "الملف غير صالح.")
        : base(
            message: message,
            errorCode: "INVALID_FILE_DATA",
            status: 400)
    {
    }
}
