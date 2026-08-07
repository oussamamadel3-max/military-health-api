using Domain.Exceptions;

namespace Application.Exceptions;

// ==========================================
// Document & Attachment Domain Custom Exceptions
// ==========================================

public class DocumentNotFoundException : BusinessException
{
    public DocumentNotFoundException(string identifier = "الوثيقة غير موجودة.")
        : base(
            message: identifier,
            errorCode: "DOCUMENT_NOT_FOUND",
            status: 404)
    {
    }
}

public class AttachmentNotFoundException : BusinessException
{
    public AttachmentNotFoundException(string identifier = "المرفق غير موجود.")
        : base(
            message: identifier,
            errorCode: "ATTACHMENT_NOT_FOUND",
            status: 404)
    {
    }
}

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

public class InvalidDocumentDataException : BusinessException
{
    public InvalidDocumentDataException(string message)
        : base(
            message: message,
            errorCode: "DOCUMENT_INVALID_DATA",
            status: 400)
    {
    }
}
