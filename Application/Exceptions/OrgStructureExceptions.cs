using Domain.Exceptions;

namespace Application.Exceptions;

public class OrgUnitTypeNotFoundException : BusinessException
{
    public OrgUnitTypeNotFoundException(string message = "نوع الوحدة التنظيمية غير موجود.")
        : base(message, "ORG_UNIT_TYPE_NOT_FOUND", 404) { }
}

public class OrgUnitNotFoundException : BusinessException
{
    public OrgUnitNotFoundException(string message = "الوحدة التنظيمية غير موجودة.")
        : base(message, "ORG_UNIT_NOT_FOUND", 404) { }
}

public class UserOrgScopeNotFoundException : BusinessException
{
    public UserOrgScopeNotFoundException(string message = "نطاق المستخدم التنظيمي غير موجود.")
        : base(message, "USER_ORG_SCOPE_NOT_FOUND", 404) { }
}

public class InvalidOrgStructureDataException : BusinessException
{
    public InvalidOrgStructureDataException(string message)
        : base(message, "ORG_STRUCTURE_INVALID_DATA", 400) { }
}
