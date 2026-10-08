-- Adapted IAM checks. User key is text (iam.Users."Id"), not uuid.

CREATE OR REPLACE FUNCTION iam."validateUserCenterAssignment"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW."isActive" THEN
        IF NOT EXISTS (
            SELECT 1 FROM iam."Users" u
            WHERE u."Id" = NEW."userId" AND u."IsActive" = true
        ) THEN
            RAISE EXCEPTION 'Active user-center assignment requires an active user';
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM core."medicalCenter" c
            WHERE c."centerId" = NEW."centerId" AND c."isActive" = true
        ) THEN
            RAISE EXCEPTION 'Active user-center assignment requires an active medical center';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctUserCenterAssignmentDependencies"
AFTER INSERT OR UPDATE OF "userId", "centerId", "isActive" ON iam."userCenter"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION iam."validateUserCenterAssignment"();

CREATE TRIGGER "trgUserCenterDeleteGuard"
BEFORE DELETE ON iam."userCenter"
FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();

CREATE TRIGGER "trgUserClinicAssignmentDeleteGuard"
BEFORE DELETE ON iam."userClinicAssignment"
FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();

-- Role.UpdatedAt is PascalCase because the template Identity role table is unchanged.
CREATE OR REPLACE FUNCTION iam."touchRoleUpdatedAt"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW."UpdatedAt" := now();
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgRoleTouchUpdatedAt"
BEFORE UPDATE ON iam."Roles"
FOR EACH ROW EXECUTE FUNCTION iam."touchRoleUpdatedAt"();

-- Read model aligned with CenterPermissionEvaluator.
-- Permission.Name replaces official code/moduleCode/operationType, which the template permission catalog does not store.
CREATE OR REPLACE VIEW iam."vUserEffectivePermission" AS
WITH permissionSources AS (
    SELECT
        ura."UserId" AS "userId",
        p."Id" AS "permissionId",
        p."Name" AS "permissionName",
        CASE WHEN ura."CenterId" IS NULL THEN 'CENTRAL'::citext ELSE 'CENTER'::citext END AS "contextScope",
        ura."CenterId" AS "centerId",
        'ROLE'::text AS "sourceType",
        ura."RoleId" AS "sourceRoleId",
        ura."Id" AS "sourceAssignmentId",
        false AS "isDirect"
    FROM iam."UserRoleAssociations" ura
    JOIN iam."Users" u
      ON u."Id" = ura."UserId" AND u."IsActive" = true
    JOIN iam."Roles" r
      ON r."Id" = ura."RoleId" AND r."IsActive" = true
    JOIN iam."RolePermissions" rp
      ON rp."RoleId" = r."Id" AND rp."IsActive" = true
    JOIN iam."Permissions" p
      ON p."Id" = rp."PermissionId" AND p."IsActive" = true
    LEFT JOIN iam."userCenter" uc
      ON uc."userId" = ura."UserId"
     AND uc."centerId" = ura."CenterId"
     AND uc."isActive" = true
    WHERE ura."IsActive" = true
      AND (ura."CenterId" IS NULL OR uc."userId" IS NOT NULL)

    UNION ALL

    SELECT
        d."UserId",
        p."Id",
        p."Name",
        CASE WHEN d."CenterId" IS NULL THEN 'CENTRAL'::citext ELSE 'CENTER'::citext END,
        d."CenterId",
        'DIRECT'::text,
        NULL::text,
        d."Id",
        true
    FROM iam."UserPermissions" d
    JOIN iam."Users" u
      ON u."Id" = d."UserId" AND u."IsActive" = true
    JOIN iam."Permissions" p
      ON p."Id" = d."PermissionId" AND p."IsActive" = true
    LEFT JOIN iam."userCenter" uc
      ON uc."userId" = d."UserId"
     AND uc."centerId" = d."CenterId"
     AND uc."isActive" = true
    WHERE d."IsActive" = true
      AND (d."CenterId" IS NULL OR uc."userId" IS NOT NULL)
)
SELECT
    "userId",
    "permissionId",
    "permissionName",
    "contextScope",
    "centerId",
    array_agg(DISTINCT "sourceType" ORDER BY "sourceType") AS "sourceTypes",
    COALESCE(
        array_agg(DISTINCT "sourceRoleId" ORDER BY "sourceRoleId") FILTER (WHERE "sourceRoleId" IS NOT NULL),
        ARRAY[]::text[]
    ) AS "sourceRoleIds",
    array_agg(DISTINCT "sourceAssignmentId" ORDER BY "sourceAssignmentId") AS "sourceAssignmentIds",
    bool_or("isDirect") AS "hasDirectGrant"
FROM permissionSources
GROUP BY "userId", "permissionId", "permissionName", "contextScope", "centerId";

CREATE OR REPLACE VIEW audit."vCrossCenterAccessReview" AS
SELECT
    e."eventId" AS "auditEventId",
    e."actorUserId",
    COALESCE(u."FullName", u."UserName") AS "actor",
    e."contextScope",
    e."centerId" AS "localCenterId",
    lc."code" AS "localCenter",
    x."targetCenterId",
    tc."code" AS "targetCenter",
    x."accessReason",
    x."dataType",
    x."queryCriteria",
    e."occurredAt" AS "accessedAt",
    e."result"
FROM audit."crossCenterAccess" x
JOIN audit."event" e
  ON e."eventId" = x."auditEventId"
LEFT JOIN iam."Users" u
  ON u."Id" = e."actorUserId"
LEFT JOIN core."medicalCenter" lc
  ON lc."centerId" = e."centerId"
JOIN core."medicalCenter" tc
  ON tc."centerId" = x."targetCenterId";
