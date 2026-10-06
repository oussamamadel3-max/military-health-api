using Application.Exceptions;

namespace Application.Common.Security;

public static class AssignmentStamp
{
    public static string RequireActor(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId == "0")
            throw new InvalidUserDataException("المستخدم المنفّذ للإسناد غير معروف.");
        return userId;
    }
}
