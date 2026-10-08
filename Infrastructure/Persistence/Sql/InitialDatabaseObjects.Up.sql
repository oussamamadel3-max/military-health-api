CREATE OR REPLACE FUNCTION audit."preventRowMutation"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '% is append-only; % is not allowed', TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME, TG_OP;
END;
$$;

-- 6.2 Locked medical result statuses
CREATE OR REPLACE FUNCTION core."guardLockedResultStatus"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD."isLocked" THEN
        RAISE EXCEPTION 'Locked result status cannot be updated or deleted (statusId=%)', OLD."statusId";
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgResultStatusLocked"
BEFORE UPDATE OR DELETE ON core."resultStatus"
FOR EACH ROW EXECUTE FUNCTION core."guardLockedResultStatus"();

-- 6.3 Active BMI ranges must not overlap
CREATE OR REPLACE FUNCTION core."validateBmiCategoryRange"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW."isActive" AND EXISTS (
        SELECT 1
        FROM core."bmiCategory" x
        WHERE x."categoryId" <> NEW."categoryId"
          AND x."isActive" = true
          AND (NEW."maxBmi" IS NULL OR x."minBmi" < NEW."maxBmi")
          AND (x."maxBmi" IS NULL OR NEW."minBmi" < x."maxBmi")
    ) THEN
        RAISE EXCEPTION 'Active BMI category range overlaps an existing active range';
    END IF;
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctBmiCategoryNoOverlap"
AFTER INSERT OR UPDATE ON core."bmiCategory"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION core."validateBmiCategoryRange"();

-- 6.4 System reference values: no physical delete and no code change
CREATE OR REPLACE FUNCTION core."guardSystemReferenceValue"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD."isSystem" THEN
            RAISE EXCEPTION 'System reference value cannot be deleted';
        END IF;
        RETURN OLD;
    END IF;

    IF OLD."isSystem" AND NEW."code" IS DISTINCT FROM OLD."code" THEN
        RAISE EXCEPTION 'Code of a system reference value cannot be changed';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgMaritalStatusSystemGuard"
BEFORE UPDATE OR DELETE ON core."maritalStatus"
FOR EACH ROW EXECUTE FUNCTION core."guardSystemReferenceValue"();

CREATE TRIGGER "trgBloodTypeSystemGuard"
BEFORE UPDATE OR DELETE ON core."bloodType"
FOR EACH ROW EXECUTE FUNCTION core."guardSystemReferenceValue"();

CREATE TRIGGER "trgConsultationTypeSystemGuard"
BEFORE UPDATE OR DELETE ON core."consultationType"
FOR EACH ROW EXECUTE FUNCTION core."guardSystemReferenceValue"();

CREATE TRIGGER "trgInvestigationTypeSystemGuard"
BEFORE UPDATE OR DELETE ON core."investigationType"
FOR EACH ROW EXECUTE FUNCTION core."guardSystemReferenceValue"();

-- 6.5 Validation-rule validity periods must not overlap for the same active code
CREATE OR REPLACE FUNCTION core."validateRulePeriod"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW."isActive" AND EXISTS (
        SELECT 1
        FROM core."validationRule" x
        WHERE x."validationRuleId" <> NEW."validationRuleId"
          AND x."code" = NEW."code"
          AND x."isActive" = true
          AND (NEW."validTo" IS NULL OR x."validFrom" < NEW."validTo")
          AND (x."validTo" IS NULL OR NEW."validFrom" < x."validTo")
    ) THEN
        RAISE EXCEPTION 'Active validation-rule periods overlap for code %', NEW."code";
    END IF;
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctValidationRuleNoOverlap"
AFTER INSERT OR UPDATE ON core."validationRule"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION core."validateRulePeriod"();

-- 6.7 Password history is append-only
CREATE TRIGGER "trgPasswordHistoryAppendOnly"
BEFORE UPDATE OR DELETE ON iam."userPasswordHistory"
FOR EACH ROW EXECUTE FUNCTION audit."preventRowMutation"();

-- 6.8 Hard-delete guards for center, account, role and IAM assignment history
CREATE OR REPLACE FUNCTION audit."preventHardDelete"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '% cannot be physically deleted by the operational schema; use the documented deactivate/revoke/close/archive path', TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME;
END;
$$;

CREATE TRIGGER "trgMedicalCenterDeleteGuard"
BEFORE DELETE ON core."medicalCenter"
FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();

CREATE OR REPLACE FUNCTION patient."guardQueueOriginCenter"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW."queueMedicalCenterId" IS DISTINCT FROM OLD."queueMedicalCenterId" THEN
        RAISE EXCEPTION 'queueMedicalCenterId is historical and cannot be changed after creation';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgMemberQueueOriginImmutable"
BEFORE UPDATE OF "queueMedicalCenterId" ON patient."member"
FOR EACH ROW EXECUTE FUNCTION patient."guardQueueOriginCenter"();

-- currentMedicalCenterId may only change through the transfer function below
CREATE OR REPLACE FUNCTION patient."guardDirectCurrentCenterChange"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW."currentMedicalCenterId" IS DISTINCT FROM OLD."currentMedicalCenterId"
       AND COALESCE(current_setting('app.memberTransfer', true), 'false') <> 'true' THEN
        RAISE EXCEPTION 'currentMedicalCenterId may only be changed through patient.transferMember';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgMemberCurrentCenterGuard"
BEFORE UPDATE OF "currentMedicalCenterId" ON patient."member"
FOR EACH ROW EXECUTE FUNCTION patient."guardDirectCurrentCenterChange"();

-- 6.12 Active institutional validation rules for age/measurements
CREATE OR REPLACE FUNCTION core."assertValidationRuleValue"(
    pTargetEntity text,
    pTargetFields text[],
    pValue numeric,
    pAt timestamptz DEFAULT now()
)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    r record;
BEGIN
    IF pValue IS NULL THEN
        RETURN;
    END IF;

    FOR r IN
        SELECT vr."code", vr."minValue", vr."maxValue"
          FROM core."validationRule" vr
         WHERE vr."isActive" = true
           AND vr."targetEntity" = pTargetEntity
           AND vr."targetField" = ANY (pTargetFields)
           AND vr."validFrom" <= pAt
           AND (vr."validTo" IS NULL OR pAt < vr."validTo")
         ORDER BY vr."versionNo" DESC
    LOOP
        IF r."minValue" IS NOT NULL AND pValue < r."minValue" THEN
            RAISE EXCEPTION 'Validation rule %: value % is below minimum %',
                r."code", pValue, r."minValue";
        END IF;
        IF r."maxValue" IS NOT NULL AND pValue > r."maxValue" THEN
            RAISE EXCEPTION 'Validation rule %: value % is above maximum %',
                r."code", pValue, r."maxValue";
        END IF;
    END LOOP;
END;
$$;

CREATE OR REPLACE FUNCTION patient."validateMemberMeasurements"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vAge numeric;
BEGIN
    vAge := date_part('year', age(CURRENT_DATE, NEW."birthDate"))::numeric;

    PERFORM core."assertValidationRuleValue"(
        'patient.member', ARRAY['age', 'birth_date', 'birthDate'], vAge, now()
    );
    PERFORM core."assertValidationRuleValue"(
        'patient.member', ARRAY['blood_sugar_mg_dl', 'bloodSugarMgDl'], NEW."bloodSugarMgDl", now()
    );
    PERFORM core."assertValidationRuleValue"(
        'patient.member', ARRAY['height_cm', 'heightCm'], NEW."heightCm", now()
    );
    PERFORM core."assertValidationRuleValue"(
        'patient.member', ARRAY['weight_kg', 'weightKg'], NEW."weightKg", now()
    );
    PERFORM core."assertValidationRuleValue"(
        'patient.member', ARRAY['pulse_bpm', 'pulseBpm'], NEW."pulseBpm"::numeric, now()
    );
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgMemberValidationRules"
BEFORE INSERT OR UPDATE OF "birthDate", "bloodSugarMgDl", "heightCm", "weightKg", "pulseBpm"
ON patient."member"
FOR EACH ROW EXECUTE FUNCTION patient."validateMemberMeasurements"();

-- 6.13 Exactly one open local identity matching the member's current medical center
CREATE OR REPLACE FUNCTION patient."assertMemberCurrentIdentity"(pMemberId uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    vCurrentCenter uuid;
    vCount integer;
    vIdentityCenter uuid;
BEGIN
    SELECT m."currentMedicalCenterId"
      INTO vCurrentCenter
      FROM patient."member" m
     WHERE m."memberId" = pMemberId;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    SELECT count(*)
      INTO vCount
      FROM patient."memberCenterIdentity" i
     WHERE i."memberId" = pMemberId
       AND i."validTo" IS NULL;

    IF vCount <> 1 THEN
        RAISE EXCEPTION 'Member % must have exactly one open memberCenterIdentity; found %', pMemberId, vCount;
    END IF;

    SELECT i."medicalCenterId"
      INTO vIdentityCenter
      FROM patient."memberCenterIdentity" i
     WHERE i."memberId" = pMemberId
       AND i."validTo" IS NULL;

    IF vIdentityCenter IS DISTINCT FROM vCurrentCenter THEN
        RAISE EXCEPTION 'Open local identity center % does not match member current center %',
            vIdentityCenter, vCurrentCenter;
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION patient."validateCurrentIdentityFromIdentity"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM patient."assertMemberCurrentIdentity"(OLD."memberId");
        RETURN OLD;
    END IF;
    PERFORM patient."assertMemberCurrentIdentity"(NEW."memberId");
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctMemberCenterIdentityCurrent"
AFTER INSERT OR UPDATE OR DELETE ON patient."memberCenterIdentity"
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION patient."validateCurrentIdentityFromIdentity"();

CREATE OR REPLACE FUNCTION patient."validateCurrentIdentityFromMember"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM patient."assertMemberCurrentIdentity"(NEW."memberId");
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctMemberHasCurrentIdentity"
AFTER INSERT OR UPDATE OF "currentMedicalCenterId" ON patient."member"
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION patient."validateCurrentIdentityFromMember"();

-- 6.14 Transfer consistency
CREATE OR REPLACE FUNCTION patient."validateMemberTransfer"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vCurrentCenter uuid;
    vFromValidTo timestamptz;
    vToValidFrom timestamptz;
    vToValidTo timestamptz;
BEGIN
    SELECT m."currentMedicalCenterId"
      INTO vCurrentCenter
      FROM patient."member" m
     WHERE m."memberId" = NEW."memberId";

    SELECT i."validTo"
      INTO vFromValidTo
      FROM patient."memberCenterIdentity" i
     WHERE i."memberCenterIdentityId" = NEW."fromMemberCenterIdentityId"
       AND i."memberId" = NEW."memberId"
       AND i."medicalCenterId" = NEW."fromCenterId";

    SELECT i."validFrom", i."validTo"
      INTO vToValidFrom, vToValidTo
      FROM patient."memberCenterIdentity" i
     WHERE i."memberCenterIdentityId" = NEW."toMemberCenterIdentityId"
       AND i."memberId" = NEW."memberId"
       AND i."medicalCenterId" = NEW."toCenterId";

    IF vFromValidTo IS DISTINCT FROM NEW."executedAt" THEN
        RAISE EXCEPTION 'Source identity must close exactly at transfer executedAt';
    END IF;

    IF vToValidFrom IS DISTINCT FROM NEW."executedAt" OR vToValidTo IS NOT NULL THEN
        RAISE EXCEPTION 'Destination identity must start at executedAt and remain open';
    END IF;

    IF vCurrentCenter IS DISTINCT FROM NEW."toCenterId" THEN
        RAISE EXCEPTION 'Member current center must equal transfer destination center';
    END IF;

    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctMemberTransferConsistency"
AFTER INSERT ON patient."memberTransfer"
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION patient."validateMemberTransfer"();

CREATE TRIGGER "trgMemberTransferAppendOnly"
BEFORE UPDATE OR DELETE ON patient."memberTransfer"
FOR EACH ROW EXECUTE FUNCTION audit."preventRowMutation"();

-- 6.15 Clinic-detail rows must match the exam clinic
CREATE OR REPLACE FUNCTION clinical."validateExamClinicCode"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vCode citext;
BEGIN
    SELECT c."code"
      INTO vCode
      FROM clinical."medicalExam" e
      JOIN core."clinic" c ON c."clinicId" = e."clinicId"
     WHERE e."examId" = NEW."examId";

    IF vCode IS DISTINCT FROM TG_ARGV[0]::citext THEN
        RAISE EXCEPTION 'Exam % belongs to clinic %, expected %', NEW."examId", vCode, TG_ARGV[0];
    END IF;
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctEyeExamDetailClinic"
AFTER INSERT OR UPDATE ON clinical."eyeExamDetail"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION clinical."validateExamClinicCode"('EYE');

CREATE CONSTRAINT TRIGGER "ctInternalExamDetailClinic"
AFTER INSERT OR UPDATE ON clinical."internalExamDetail"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION clinical."validateExamClinicCode"('INTERNAL');

CREATE CONSTRAINT TRIGGER "ctSurgeryExamDetailClinic"
AFTER INSERT OR UPDATE ON clinical."surgeryExamDetail"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION clinical."validateExamClinicCode"('SURGERY');

CREATE CONSTRAINT TRIGGER "ctOrthopedicExamDetailClinic"
AFTER INSERT OR UPDATE ON clinical."orthopedicExamDetail"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION clinical."validateExamClinicCode"('ORTHOPEDIC');

CREATE CONSTRAINT TRIGGER "ctEntExamDetailClinic"
AFTER INSERT OR UPDATE ON clinical."entExamDetail"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION clinical."validateExamClinicCode"('ENT');

CREATE OR REPLACE FUNCTION clinical."guardExamClinicChangeAgainstDetails"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vNewCode citext;
BEGIN
    IF NEW."clinicId" IS NOT DISTINCT FROM OLD."clinicId" THEN
        RETURN NEW;
    END IF;

    SELECT c."code" INTO vNewCode
      FROM core."clinic" c
     WHERE c."clinicId" = NEW."clinicId";

    IF EXISTS (SELECT 1 FROM clinical."eyeExamDetail" d WHERE d."examId" = NEW."examId")
       AND vNewCode <> 'EYE' THEN
        RAISE EXCEPTION 'Cannot change exam clinic: EYE detail exists';
    END IF;
    IF EXISTS (SELECT 1 FROM clinical."internalExamDetail" d WHERE d."examId" = NEW."examId")
       AND vNewCode <> 'INTERNAL' THEN
        RAISE EXCEPTION 'Cannot change exam clinic: INTERNAL detail exists';
    END IF;
    IF EXISTS (SELECT 1 FROM clinical."surgeryExamDetail" d WHERE d."examId" = NEW."examId")
       AND vNewCode <> 'SURGERY' THEN
        RAISE EXCEPTION 'Cannot change exam clinic: SURGERY detail exists';
    END IF;
    IF EXISTS (SELECT 1 FROM clinical."orthopedicExamDetail" d WHERE d."examId" = NEW."examId")
       AND vNewCode <> 'ORTHOPEDIC' THEN
        RAISE EXCEPTION 'Cannot change exam clinic: ORTHOPEDIC detail exists';
    END IF;
    IF EXISTS (SELECT 1 FROM clinical."entExamDetail" d WHERE d."examId" = NEW."examId")
       AND vNewCode <> 'ENT' THEN
        RAISE EXCEPTION 'Cannot change exam clinic: ENT detail exists';
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgMedicalExamClinicDetailGuard"
BEFORE UPDATE OF "clinicId" ON clinical."medicalExam"
FOR EACH ROW EXECUTE FUNCTION clinical."guardExamClinicChangeAgainstDetails"();

-- 6.16 Deferred-action/source consistency
CREATE OR REPLACE FUNCTION clinical."assertAssessmentDeferral"(pAssessmentId uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    vStatus smallint;
    vOpenCount integer;
BEGIN
    SELECT a."resultStatusId" INTO vStatus
      FROM clinical."clinicAssessment" a
     WHERE a."assessmentId" = pAssessmentId;

    IF NOT FOUND THEN RETURN; END IF;

    SELECT count(*) INTO vOpenCount
      FROM clinical."deferredAction" d
     WHERE d."clinicAssessmentId" = pAssessmentId
       AND d."status" <> 'CLOSED';

    IF vStatus = 3 AND vOpenCount <> 1 THEN
        RAISE EXCEPTION 'Deferred clinic assessment % must have exactly one open deferredAction', pAssessmentId;
    ELSIF vStatus <> 3 AND vOpenCount <> 0 THEN
        RAISE EXCEPTION 'Non-deferred clinic assessment % cannot have an open deferredAction', pAssessmentId;
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION clinical."assertFinalDecisionDeferral"(pFinalDecisionId uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    vStatus smallint;
    vOpenCount integer;
BEGIN
    SELECT f."resultStatusId" INTO vStatus
      FROM clinical."finalDecision" f
     WHERE f."finalDecisionId" = pFinalDecisionId;

    IF NOT FOUND THEN RETURN; END IF;

    SELECT count(*) INTO vOpenCount
      FROM clinical."deferredAction" d
     WHERE d."finalDecisionId" = pFinalDecisionId
       AND d."status" <> 'CLOSED';

    IF vStatus = 3 AND vOpenCount <> 1 THEN
        RAISE EXCEPTION 'Deferred final decision % must have exactly one open deferredAction', pFinalDecisionId;
    ELSIF vStatus <> 3 AND vOpenCount <> 0 THEN
        RAISE EXCEPTION 'Non-deferred final decision % cannot have an open deferredAction', pFinalDecisionId;
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION clinical."validateDeferredActionSource"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP <> 'INSERT' AND OLD."clinicAssessmentId" IS NOT NULL THEN
        PERFORM clinical."assertAssessmentDeferral"(OLD."clinicAssessmentId");
    END IF;
    IF TG_OP <> 'INSERT' AND OLD."finalDecisionId" IS NOT NULL THEN
        PERFORM clinical."assertFinalDecisionDeferral"(OLD."finalDecisionId");
    END IF;
    IF TG_OP <> 'DELETE' AND NEW."clinicAssessmentId" IS NOT NULL THEN
        PERFORM clinical."assertAssessmentDeferral"(NEW."clinicAssessmentId");
    END IF;
    IF TG_OP <> 'DELETE' AND NEW."finalDecisionId" IS NOT NULL THEN
        PERFORM clinical."assertFinalDecisionDeferral"(NEW."finalDecisionId");
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctDeferredActionSourceState"
AFTER INSERT OR UPDATE OR DELETE ON clinical."deferredAction"
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION clinical."validateDeferredActionSource"();

CREATE OR REPLACE FUNCTION clinical."validateAssessmentDeferralAtCommit"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM clinical."assertAssessmentDeferral"(NEW."assessmentId");
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctClinicAssessmentDeferral"
AFTER INSERT OR UPDATE OF "resultStatusId" ON clinical."clinicAssessment"
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION clinical."validateAssessmentDeferralAtCommit"();

CREATE OR REPLACE FUNCTION clinical."validateFinalDecisionDeferralAtCommit"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM clinical."assertFinalDecisionDeferral"(NEW."finalDecisionId");
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctFinalDecisionDeferral"
AFTER INSERT OR UPDATE OF "resultStatusId" ON clinical."finalDecision"
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION clinical."validateFinalDecisionDeferralAtCommit"();

CREATE OR REPLACE FUNCTION clinical."closeOpenDeferralWhenSourceChanges"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD."resultStatusId" = 3 AND NEW."resultStatusId" <> 3 THEN
        IF TG_TABLE_NAME = 'clinicAssessment' THEN
            UPDATE clinical."deferredAction"
               SET "status" = 'CLOSED',
                   "closedAt" = COALESCE("closedAt", now())
             WHERE "clinicAssessmentId" = NEW."assessmentId"
               AND "status" <> 'CLOSED';
        ELSIF TG_TABLE_NAME = 'finalDecision' THEN
            UPDATE clinical."deferredAction"
               SET "status" = 'CLOSED',
                   "closedAt" = COALESCE("closedAt", now())
             WHERE "finalDecisionId" = NEW."finalDecisionId"
               AND "status" <> 'CLOSED';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgClinicAssessmentCloseDeferral"
BEFORE UPDATE OF "resultStatusId" ON clinical."clinicAssessment"
FOR EACH ROW EXECUTE FUNCTION clinical."closeOpenDeferralWhenSourceChanges"();

CREATE TRIGGER "trgFinalDecisionCloseDeferral"
BEFORE UPDATE OF "resultStatusId" ON clinical."finalDecision"
FOR EACH ROW EXECUTE FUNCTION clinical."closeOpenDeferralWhenSourceChanges"();

-- 6.17 Deferred-action type drives target clinic/description requirements
CREATE OR REPLACE FUNCTION clinical."validateDeferredActionTypeRequirements"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vRequiresTarget boolean;
    vRequiresDescription boolean;
BEGIN
    SELECT a."requiresTargetClinic", a."requiresDescription"
      INTO vRequiresTarget, vRequiresDescription
      FROM core."requiredActionType" a
     WHERE a."actionTypeId" = NEW."actionTypeId";

    IF NOT FOUND THEN
        RAISE EXCEPTION 'requiredActionType % does not exist', NEW."actionTypeId";
    END IF;

    IF vRequiresTarget AND NEW."targetClinicId" IS NULL THEN
        RAISE EXCEPTION 'This action type requires targetClinicId';
    ELSIF NOT vRequiresTarget AND NEW."targetClinicId" IS NOT NULL THEN
        RAISE EXCEPTION 'This action type does not allow targetClinicId';
    END IF;

    IF vRequiresDescription
       AND (NEW."actionDescription" IS NULL OR btrim(NEW."actionDescription") = '') THEN
        RAISE EXCEPTION 'This action type requires a non-empty actionDescription';
    ELSIF NOT vRequiresDescription
       AND NEW."actionDescription" IS NOT NULL
       AND btrim(NEW."actionDescription") <> '' THEN
        RAISE EXCEPTION 'This action type does not allow actionDescription';
    END IF;

    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctDeferredActionTypeRequirements"
AFTER INSERT OR UPDATE OF "actionTypeId", "targetClinicId", "actionDescription"
ON clinical."deferredAction"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION clinical."validateDeferredActionTypeRequirements"();

-- 6.18 Final decision requires all active required clinics to be assessed
CREATE OR REPLACE FUNCTION clinical."validateFinalDecisionCompleteness"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vRequired integer;
    vCompleted integer;
BEGIN
    SELECT count(*)
      INTO vRequired
      FROM core."centerClinic" cc
      JOIN core."clinic" c ON c."clinicId" = cc."clinicId"
     WHERE cc."centerId" = NEW."centerId"
       AND cc."isActive" = true
       AND c."isActive" = true
       AND c."countsForFinal" = true;

    SELECT count(DISTINCT a."clinicId")
      INTO vCompleted
      FROM clinical."clinicAssessment" a
      JOIN clinical."medicalExam" e ON e."examId" = a."examId"
     WHERE a."memberId" = NEW."memberId"
       AND a."centerId" = NEW."centerId"
       AND e."isCurrent" = true
       AND EXISTS (
           SELECT 1
           FROM core."centerClinic" cc
           JOIN core."clinic" c ON c."clinicId" = cc."clinicId"
           WHERE cc."centerId" = NEW."centerId"
             AND cc."clinicId" = a."clinicId"
             AND cc."isActive" = true
             AND c."isActive" = true
             AND c."countsForFinal" = true
       );

    IF vCompleted <> vRequired THEN
        RAISE EXCEPTION 'Final decision requires all required clinic assessments (%/% complete)', vCompleted, vRequired;
    END IF;

    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctFinalDecisionCompleteness"
AFTER INSERT OR UPDATE OF "memberId", "centerId" ON clinical."finalDecision"
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION clinical."validateFinalDecisionCompleteness"();

-- 6.19 Final decision disagreement requires justification
CREATE OR REPLACE FUNCTION clinical."validateFinalDecisionDisagreement"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vHasDisagreement boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM clinical."clinicAssessment" a
        JOIN clinical."medicalExam" e ON e."examId" = a."examId"
        JOIN core."clinic" c ON c."clinicId" = a."clinicId"
        JOIN core."centerClinic" cc
          ON cc."centerId" = NEW."centerId"
         AND cc."clinicId" = a."clinicId"
        WHERE a."memberId" = NEW."memberId"
          AND a."centerId" = NEW."centerId"
          AND e."isCurrent" = true
          AND c."countsForFinal" = true
          AND c."isActive" = true
          AND cc."isActive" = true
          AND a."resultStatusId" <> NEW."resultStatusId"
    ) INTO vHasDisagreement;

    IF vHasDisagreement
       AND (NEW."disagreementJustification" IS NULL OR btrim(NEW."disagreementJustification") = '') THEN
        RAISE EXCEPTION 'disagreementJustification is required when final result differs from a required clinic assessment';
    END IF;

    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctFinalDecisionDisagreement"
AFTER INSERT OR UPDATE OF "resultStatusId", "disagreementJustification", "memberId", "centerId"
ON clinical."finalDecision"
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION clinical."validateFinalDecisionDisagreement"();

-- Final decision must have exactly one matching archive row before COMMIT.
CREATE OR REPLACE FUNCTION clinical."validateFinalDecisionArchive"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vCount integer;
BEGIN
    SELECT count(*) INTO vCount
    FROM archive."memberArchive" a
    WHERE a."finalDecisionId" = NEW."finalDecisionId"
      AND a."memberId" = NEW."memberId"
      AND a."centerId" = NEW."centerId";

    IF vCount <> 1 THEN
        RAISE EXCEPTION 'Final decision % requires exactly one matching archive.memberArchive before COMMIT', NEW."finalDecisionId";
    END IF;
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctFinalDecisionArchive"
AFTER INSERT OR UPDATE OF "memberId", "centerId" ON clinical."finalDecision"
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION clinical."validateFinalDecisionArchive"();

CREATE OR REPLACE FUNCTION clinical."syncFinalDecisionArchiveUpdate"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE archive."memberArchive"
       SET "memberId" = NEW."memberId",
           "centerId" = NEW."centerId",
           "updatedAt" = now()
     WHERE "finalDecisionId" = NEW."finalDecisionId";
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgFinalDecisionArchiveSync"
AFTER UPDATE OF "memberId", "centerId", "resultStatusId", "rejectionReason", "disagreementJustification"
ON clinical."finalDecision"
FOR EACH ROW EXECUTE FUNCTION clinical."syncFinalDecisionArchiveUpdate"();

-- 6.20 Final-decision history is written automatically when result status changes.
-- Application should SET LOCAL app.changeReason = '...' for the transaction.
CREATE OR REPLACE FUNCTION clinical."writeFinalDecisionHistory"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vReason text;
    vActor text;
BEGIN
    IF NEW."resultStatusId" IS NOT DISTINCT FROM OLD."resultStatusId" THEN
        RETURN NEW;
    END IF;

    vReason := NULLIF(current_setting('app.changeReason', true), '');
    IF vReason IS NULL THEN
        RAISE EXCEPTION 'SET LOCAL app.changeReason before changing finalDecision.resultStatusId';
    END IF;

    vActor := COALESCE(
        NULLIF(current_setting('app.currentUserId', true), ''),
        NEW."updatedByUserId"
    );

    INSERT INTO clinical."finalDecisionHistory" (
        "historyId", "finalDecisionId", "memberId", "centerId",
        "oldStatusId", "newStatusId", "changeReason",
        "changedByUserId", "changedAt", "exportResetPerformed"
    )
    VALUES (
        gen_random_uuid(), NEW."finalDecisionId", NEW."memberId", NEW."centerId",
        OLD."resultStatusId", NEW."resultStatusId", vReason,
        vActor, now(), true
    );

    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgFinalDecisionHistory"
AFTER UPDATE OF "resultStatusId" ON clinical."finalDecision"
FOR EACH ROW EXECUTE FUNCTION clinical."writeFinalDecisionHistory"();

-- 6.21 Export-item center must match the final-decision center
CREATE OR REPLACE FUNCTION export."validateExportItemCenter"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vBatchCenter uuid;
    vDecisionCenter uuid;
BEGIN
    SELECT b."centerId" INTO vBatchCenter
      FROM export."recruitmentExportBatch" b
     WHERE b."exportBatchId" = NEW."exportBatchId";

    SELECT f."centerId" INTO vDecisionCenter
      FROM clinical."finalDecision" f
     WHERE f."finalDecisionId" = NEW."finalDecisionId";

    IF vBatchCenter IS DISTINCT FROM vDecisionCenter THEN
        RAISE EXCEPTION 'Export batch center does not match final decision center';
    END IF;

    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctExportItemCenter"
AFTER INSERT OR UPDATE OF "exportBatchId", "finalDecisionId"
ON export."recruitmentExportItem"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION export."validateExportItemCenter"();

-- 6.22 Cross-center access must refer to a VIEW event with a known actor,
-- and local center (when present) must differ from target center.
CREATE OR REPLACE FUNCTION audit."validateCrossCenterAccess"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vEventType citext;
    vEventResult citext;
    vActor text;
    vLocalCenter uuid;
BEGIN
    SELECT e."eventType", e."result", e."actorUserId", e."centerId"
      INTO vEventType, vEventResult, vActor, vLocalCenter
      FROM audit."event" e
     WHERE e."eventId" = NEW."auditEventId";

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Parent audit event % does not exist', NEW."auditEventId";
    END IF;

    IF vEventType <> 'VIEW' THEN
        RAISE EXCEPTION 'Cross-center access parent event must have eventType=VIEW';
    END IF;

    IF vActor IS NULL THEN
        RAISE EXCEPTION 'Cross-center access parent event must identify actorUserId';
    END IF;

    IF vEventResult NOT IN ('SUCCESS','DENIED') THEN
        RAISE EXCEPTION 'Cross-center access parent event result must be SUCCESS or DENIED';
    END IF;

    IF vLocalCenter IS NOT NULL AND vLocalCenter = NEW."targetCenterId" THEN
        RAISE EXCEPTION 'Cross-center access target must differ from local center';
    END IF;

    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER "ctCrossCenterAccessParent"
AFTER INSERT OR UPDATE OF "auditEventId", "targetCenterId"
ON audit."crossCenterAccess"
DEFERRABLE INITIALLY IMMEDIATE
FOR EACH ROW EXECUTE FUNCTION audit."validateCrossCenterAccess"();

CREATE TRIGGER "trgAuditEventAppendOnly"
BEFORE UPDATE OR DELETE ON audit."event"
FOR EACH ROW EXECUTE FUNCTION audit."preventRowMutation"();

CREATE TRIGGER "trgCrossCenterAccessAppendOnly"
BEFORE UPDATE OR DELETE ON audit."crossCenterAccess"
FOR EACH ROW EXECUTE FUNCTION audit."preventRowMutation"();

-- 6.23 Member revision/export-state control
CREATE OR REPLACE FUNCTION patient."prepareMemberRevision"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vActorText text;
BEGIN
    IF COALESCE(current_setting('app.suppressRevision', true), 'false') = 'true' THEN
        RETURN NEW;
    END IF;

    IF (
        to_jsonb(NEW) - ARRAY[
            'dataRevision','exportStatus','lastExportedRevision','lastExportedAt',
            'reviewedByUserId','reviewedAt','reviewedRevision',
            'updatedByUserId','updatedAt'
        ]
    ) IS DISTINCT FROM (
        to_jsonb(OLD) - ARRAY[
            'dataRevision','exportStatus','lastExportedRevision','lastExportedAt',
            'reviewedByUserId','reviewedAt','reviewedRevision',
            'updatedByUserId','updatedAt'
        ]
    ) THEN
        NEW."dataRevision" := OLD."dataRevision" + 1;
        NEW."exportStatus" := 'UNEXPORTED';
        NEW."lastExportedAt" := NULL;
        NEW."updatedAt" := now();

        vActorText := NULLIF(current_setting('app.currentUserId', true), '');
        IF vActorText IS NOT NULL THEN
            NEW."updatedByUserId" := vActorText;
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgMemberRevisionOnMaterialUpdate"
BEFORE UPDATE ON patient."member"
FOR EACH ROW EXECUTE FUNCTION patient."prepareMemberRevision"();

CREATE OR REPLACE FUNCTION patient."validateMemberExportState"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW."exportStatus" = 'UNEXPORTED' AND NEW."lastExportedAt" IS NOT NULL THEN
        RAISE EXCEPTION 'UNEXPORTED requires lastExportedAt = NULL for the current revision';
    END IF;

    IF NEW."exportStatus" = 'EXPORTED' THEN
        IF NEW."reviewedRevision" IS DISTINCT FROM NEW."dataRevision" THEN
            RAISE EXCEPTION 'Current member revision must be reviewed before export';
        END IF;
        IF NEW."lastExportedRevision" IS DISTINCT FROM NEW."dataRevision"
           OR NEW."lastExportedAt" IS NULL THEN
            RAISE EXCEPTION 'EXPORTED requires lastExportedRevision=dataRevision and lastExportedAt';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgMemberExportState"
BEFORE INSERT OR UPDATE OF "exportStatus", "dataRevision", "reviewedRevision", "lastExportedRevision", "lastExportedAt"
ON patient."member"
FOR EACH ROW EXECUTE FUNCTION patient."validateMemberExportState"();

CREATE OR REPLACE FUNCTION patient."bumpMemberRevisionById"(pMemberId uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    vActorText text;
BEGIN
    IF pMemberId IS NULL
       OR COALESCE(current_setting('app.suppressRevision', true), 'false') = 'true' THEN
        RETURN;
    END IF;

    vActorText := NULLIF(current_setting('app.currentUserId', true), '');

    UPDATE patient."member"
       SET "dataRevision" = "dataRevision" + 1,
           "exportStatus" = 'UNEXPORTED',
           "lastExportedAt" = NULL,
           "updatedAt" = now(),
           "updatedByUserId" = CASE
               WHEN vActorText IS NULL THEN "updatedByUserId"
               ELSE vActorText
           END
     WHERE "memberId" = pMemberId;
END;
$$;

CREATE OR REPLACE FUNCTION patient."bumpRevisionFromMemberChild"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM patient."bumpMemberRevisionById"(OLD."memberId");
        RETURN OLD;
    END IF;

    PERFORM patient."bumpMemberRevisionById"(NEW."memberId");

    IF TG_OP = 'UPDATE' AND OLD."memberId" IS DISTINCT FROM NEW."memberId" THEN
        PERFORM patient."bumpMemberRevisionById"(OLD."memberId");
    END IF;
    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION patient."bumpRevisionFromExamDetail"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vExamId uuid;
    vMemberId uuid;
BEGIN
    vExamId := CASE WHEN TG_OP = 'DELETE' THEN OLD."examId" ELSE NEW."examId" END;

    SELECT e."memberId" INTO vMemberId
      FROM clinical."medicalExam" e
     WHERE e."examId" = vExamId;

    PERFORM patient."bumpMemberRevisionById"(vMemberId);
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION patient."bumpRevisionFromRefraction"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    vExamId uuid;
    vMemberId uuid;
BEGIN
    vExamId := CASE WHEN TG_OP = 'DELETE' THEN OLD."examId" ELSE NEW."examId" END;

    SELECT e."memberId" INTO vMemberId
      FROM clinical."medicalExam" e
     WHERE e."examId" = vExamId;

    PERFORM patient."bumpMemberRevisionById"(vMemberId);
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

-- Direct-member child tables that materially change the unified file.
CREATE TRIGGER "trgMemberIdentityRevision"
AFTER INSERT OR UPDATE OR DELETE ON patient."memberCenterIdentity"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

CREATE TRIGGER "trgMemberGeneralNoteRevision"
AFTER INSERT OR UPDATE OR DELETE ON patient."memberGeneralNote"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

CREATE TRIGGER "trgMedicalExamRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."medicalExam"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

CREATE TRIGGER "trgClinicAssessmentRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."clinicAssessment"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

CREATE TRIGGER "trgDeferredActionRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."deferredAction"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

CREATE TRIGGER "trgFinalDecisionRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."finalDecision"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

CREATE TRIGGER "trgSupervisorObjectionRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."supervisorObjection"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

CREATE TRIGGER "trgConsultationRevision"
AFTER INSERT OR UPDATE OR DELETE ON medical."consultation"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

CREATE TRIGGER "trgInvestigationRevision"
AFTER INSERT OR UPDATE OR DELETE ON medical."investigation"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

CREATE TRIGGER "trgMedicalAttachmentRevision"
AFTER INSERT OR UPDATE OR DELETE ON medical."medicalAttachment"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromMemberChild"();

-- Clinic-specific detail tables.
CREATE TRIGGER "trgEyeExamDetailRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."eyeExamDetail"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromExamDetail"();

CREATE TRIGGER "trgInternalExamDetailRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."internalExamDetail"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromExamDetail"();

CREATE TRIGGER "trgSurgeryExamDetailRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."surgeryExamDetail"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromExamDetail"();

CREATE TRIGGER "trgOrthopedicExamDetailRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."orthopedicExamDetail"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromExamDetail"();

CREATE TRIGGER "trgEntExamDetailRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."entExamDetail"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromExamDetail"();

CREATE TRIGGER "trgEyeRefractionRevision"
AFTER INSERT OR UPDATE OR DELETE ON clinical."eyeRefractionDetail"
FOR EACH ROW EXECUTE FUNCTION patient."bumpRevisionFromRefraction"();

-- 6.24 Atomic member transfer entry point
CREATE OR REPLACE FUNCTION patient."transferMember"(
    pMemberId uuid,
    pToCenterId uuid,
    pNewCenterMemberId citext,
    pExecutedByUserId text,
    pTransferReason text,
    pExecutedAt timestamptz DEFAULT now()
)
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    vFromCenterId uuid;
    vFromIdentityId uuid;
    vToIdentityId uuid := gen_random_uuid();
    vTransferId uuid := gen_random_uuid();
BEGIN
    IF pTransferReason IS NULL OR btrim(pTransferReason) = '' THEN
        RAISE EXCEPTION 'transferReason is required';
    END IF;
    IF pNewCenterMemberId IS NULL OR btrim(pNewCenterMemberId::text) = '' THEN
        RAISE EXCEPTION 'new centerMemberId is required';
    END IF;

    SELECT m."currentMedicalCenterId"
      INTO vFromCenterId
      FROM patient."member" m
     WHERE m."memberId" = pMemberId
     FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Member % does not exist', pMemberId;
    END IF;

    IF vFromCenterId = pToCenterId THEN
        RAISE EXCEPTION 'Cannot transfer member to the same medical center';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM core."medicalCenter" c
        WHERE c."centerId" = pToCenterId AND c."isActive" = true
    ) THEN
        RAISE EXCEPTION 'Destination medical center must exist and be active';
    END IF;

    SELECT i."memberCenterIdentityId"
      INTO vFromIdentityId
      FROM patient."memberCenterIdentity" i
     WHERE i."memberId" = pMemberId
       AND i."medicalCenterId" = vFromCenterId
       AND i."validTo" IS NULL
     FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Current open memberCenterIdentity was not found';
    END IF;

    PERFORM set_config('app.memberTransfer', 'true', true);
    PERFORM set_config('app.suppressRevision', 'true', true);
    PERFORM set_config('app.currentUserId', pExecutedByUserId::text, true);

    UPDATE patient."memberCenterIdentity"
       SET "validTo" = pExecutedAt,
           "closedByUserId" = pExecutedByUserId
     WHERE "memberCenterIdentityId" = vFromIdentityId;

    INSERT INTO patient."memberCenterIdentity" (
        "memberCenterIdentityId", "memberId", "medicalCenterId", "centerMemberId",
        "validFrom", "validTo", "assignedByUserId", "closedByUserId"
    )
    VALUES (
        vToIdentityId, pMemberId, pToCenterId, pNewCenterMemberId,
        pExecutedAt, NULL, pExecutedByUserId, NULL
    );

    UPDATE patient."member"
       SET "currentMedicalCenterId" = pToCenterId,
           "dataRevision" = "dataRevision" + 1,
           "exportStatus" = 'UNEXPORTED',
           "lastExportedAt" = NULL,
           "updatedByUserId" = pExecutedByUserId,
           "updatedAt" = pExecutedAt
     WHERE "memberId" = pMemberId;

    INSERT INTO patient."memberTransfer" (
        "transferId", "memberId", "fromCenterId", "toCenterId",
        "fromMemberCenterIdentityId", "toMemberCenterIdentityId",
        "transferReason", "executedByUserId", "executedAt"
    )
    VALUES (
        vTransferId, pMemberId, vFromCenterId, pToCenterId,
        vFromIdentityId, vToIdentityId,
        pTransferReason, pExecutedByUserId, pExecutedAt
    );

    INSERT INTO audit."event" (
        "occurredAt", "actorUserId", "contextScope", "centerId", "eventType", "result",
        "entityType", "entityId", "reasonText", "oldValues", "newValues",
        "correlationId"
    )
    VALUES (
        pExecutedAt, pExecutedByUserId, 'CENTER', vFromCenterId, 'TRANSFER', 'SUCCESS',
        'patient.member', pMemberId::text, pTransferReason,
        jsonb_build_object('currentMedicalCenterId', vFromCenterId),
        jsonb_build_object('currentMedicalCenterId', pToCenterId),
        gen_random_uuid()
    );

    PERFORM set_config('app.suppressRevision', 'false', true);
    PERFORM set_config('app.memberTransfer', 'false', true);
    RETURN vTransferId;
END;
$$;

-- 6.25 Keep documented updatedAt columns current
CREATE OR REPLACE FUNCTION core."touchUpdatedAt"()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW."updatedAt" := now();
    RETURN NEW;
END;
$$;

CREATE TRIGGER "trgMedicalCenterTouchUpdatedAt"
BEFORE UPDATE ON core."medicalCenter"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgRecruitmentCenterTouchUpdatedAt"
BEFORE UPDATE ON core."recruitmentCenter"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgMaritalStatusTouchUpdatedAt"
BEFORE UPDATE ON core."maritalStatus"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgBloodTypeTouchUpdatedAt"
BEFORE UPDATE ON core."bloodType"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgConsultationTypeTouchUpdatedAt"
BEFORE UPDATE ON core."consultationType"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgInvestigationTypeTouchUpdatedAt"
BEFORE UPDATE ON core."investigationType"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgValidationRuleTouchUpdatedAt"
BEFORE UPDATE ON core."validationRule"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgUserProfileTouchUpdatedAt"
BEFORE UPDATE ON iam."userProfile"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgUserContactTouchUpdatedAt"
BEFORE UPDATE ON iam."userContact"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgPasswordPolicyTouchUpdatedAt"
BEFORE UPDATE ON iam."passwordPolicy"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgMemberTouchUpdatedAt"
BEFORE UPDATE ON patient."member"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgMemberGeneralNoteTouchUpdatedAt"
BEFORE UPDATE ON patient."memberGeneralNote"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgMedicalExamTouchUpdatedAt"
BEFORE UPDATE ON clinical."medicalExam"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgClinicAssessmentTouchUpdatedAt"
BEFORE UPDATE ON clinical."clinicAssessment"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgFinalDecisionTouchUpdatedAt"
BEFORE UPDATE ON clinical."finalDecision"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

CREATE TRIGGER "trgMemberArchiveTouchUpdatedAt"
BEFORE UPDATE ON archive."memberArchive"
FOR EACH ROW EXECUTE FUNCTION core."touchUpdatedAt"();

-- Operational hard-delete policy for medical records and decisions.
-- Legal destruction is outside this schema's normal operating path.
CREATE TRIGGER "trgPatientMemberNoDelete" BEFORE DELETE ON patient."member" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgPatientMemberIdentityNoDelete" BEFORE DELETE ON patient."memberCenterIdentity" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgPatientMemberTransferNoDelete" BEFORE DELETE ON patient."memberTransfer" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgPatientGeneralNoteNoDelete" BEFORE DELETE ON patient."memberGeneralNote" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgMedicalExamNoDelete" BEFORE DELETE ON clinical."medicalExam" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgClinicAssessmentNoDelete" BEFORE DELETE ON clinical."clinicAssessment" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgEyeExamDetailNoDelete" BEFORE DELETE ON clinical."eyeExamDetail" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgEyeRefractionDetailNoDelete" BEFORE DELETE ON clinical."eyeRefractionDetail" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgInternalExamDetailNoDelete" BEFORE DELETE ON clinical."internalExamDetail" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgSurgeryExamDetailNoDelete" BEFORE DELETE ON clinical."surgeryExamDetail" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgOrthopedicExamDetailNoDelete" BEFORE DELETE ON clinical."orthopedicExamDetail" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgEntExamDetailNoDelete" BEFORE DELETE ON clinical."entExamDetail" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgDeferredActionNoDelete" BEFORE DELETE ON clinical."deferredAction" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgFinalDecisionNoDelete" BEFORE DELETE ON clinical."finalDecision" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgFinalDecisionHistoryNoDelete" BEFORE DELETE ON clinical."finalDecisionHistory" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgSupervisorObjectionNoDelete" BEFORE DELETE ON clinical."supervisorObjection" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgConsultationNoDelete" BEFORE DELETE ON medical."consultation" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgInvestigationNoDelete" BEFORE DELETE ON medical."investigation" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgMedicalAttachmentNoDelete" BEFORE DELETE ON medical."medicalAttachment" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
CREATE TRIGGER "trgMemberArchiveNoDelete" BEFORE DELETE ON archive."memberArchive" FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();

CREATE OR REPLACE VIEW patient."vMemberBmi" AS
SELECT
    m."memberId",
    m."heightCm",
    m."weightKg",
    m."bmi",
    bc."categoryId" AS "bmiCategoryId",
    bc."code" AS "bmiCategoryCode",
    bc."nameAr" AS "bmiCategoryNameAr"
FROM patient."member" m
LEFT JOIN LATERAL (
    SELECT x.*
    FROM core."bmiCategory" x
    WHERE x."isActive" = true
      AND m."bmi" IS NOT NULL
      AND m."bmi" >= x."minBmi"
      AND (x."maxBmi" IS NULL OR m."bmi" < x."maxBmi")
    ORDER BY x."sortOrder"
    LIMIT 1
) bc ON true;

-- 8.3 Open deferrals
CREATE OR REPLACE VIEW clinical."vOpenDeferredMembers" AS
SELECT
    d."deferredActionId",
    d."memberId",
    m."fileNumber",
    m."currentMedicalCenterId",
    ci."centerMemberId" AS "currentCenterMemberId",
    m."recruitmentCenterId",
    CASE
        WHEN d."clinicAssessmentId" IS NOT NULL THEN 'CLINIC'
        ELSE 'FINAL_DECISION'
    END::text AS "deferralScope",
    d."clinicId",
    c."code" AS "clinicCode",
    d."targetClinicId",
    d."clinicAssessmentId",
    d."finalDecisionId",
    d."deferReason",
    d."deferUntil",
    CASE WHEN d."deferUntil" IS NULL THEN NULL ELSE (d."deferUntil" - d."requestedAt"::date) END AS "deferDurationDays",
    (d."status" = 'REQUESTED' AND d."deferUntil" IS NOT NULL AND d."deferUntil" < CURRENT_DATE) AS "isOverdue",
    d."actionTypeId",
    d."status" AS "actionStatus",
    d."followupNote",
    d."requestedByUserId",
    d."requestedAt"
FROM clinical."deferredAction" d
JOIN patient."member" m
  ON m."memberId" = d."memberId"
LEFT JOIN patient."memberCenterIdentity" ci
  ON ci."memberId" = m."memberId"
 AND ci."medicalCenterId" = m."currentMedicalCenterId"
 AND ci."validTo" IS NULL
LEFT JOIN core."clinic" c
  ON c."clinicId" = d."clinicId"
WHERE d."status" <> 'CLOSED';

-- 8.4 Five-clinic status matrix
CREATE OR REPLACE VIEW clinical."vMemberClinicStatusMatrix" AS
WITH currentAssessment AS (
    SELECT
        a."memberId",
        c."code" AS "clinicCode",
        rs."code" AS "statusCode"
    FROM clinical."clinicAssessment" a
    JOIN clinical."medicalExam" e
      ON e."examId" = a."examId"
     AND e."isCurrent" = true
    JOIN core."clinic" c
      ON c."clinicId" = a."clinicId"
    JOIN core."resultStatus" rs
      ON rs."statusId" = a."resultStatusId"
),
assessmentPivot AS (
    SELECT
        "memberId",
        max("statusCode"::text) FILTER (WHERE "clinicCode" = 'EYE') AS "eyeStatus",
        max("statusCode"::text) FILTER (WHERE "clinicCode" = 'INTERNAL') AS "internalStatus",
        max("statusCode"::text) FILTER (WHERE "clinicCode" = 'SURGERY') AS "surgeryStatus",
        max("statusCode"::text) FILTER (WHERE "clinicCode" = 'ORTHOPEDIC') AS "orthopedicStatus",
        max("statusCode"::text) FILTER (WHERE "clinicCode" = 'ENT') AS "entStatus"
    FROM currentAssessment
    GROUP BY "memberId"
),
openDeferral AS (
    SELECT
        d."memberId",
        count(*)::integer AS "openDeferralCount",
        min(d."deferUntil") AS "nearestDeferUntil"
    FROM clinical."deferredAction" d
    WHERE d."status" <> 'CLOSED'
    GROUP BY d."memberId"
)
SELECT
    m."memberId",
    m."fileNumber",
    p."eyeStatus",
    p."internalStatus",
    p."surgeryStatus",
    p."orthopedicStatus",
    p."entStatus",
    (COALESCE(o."openDeferralCount", 0) > 0) AS "hasOpenDeferral",
    COALESCE(o."openDeferralCount", 0) AS "openDeferralCount",
    o."nearestDeferUntil",
    frs."code"::text AS "finalStatus"
FROM patient."member" m
LEFT JOIN assessmentPivot p
  ON p."memberId" = m."memberId"
LEFT JOIN openDeferral o
  ON o."memberId" = m."memberId"
LEFT JOIN clinical."finalDecision" f
  ON f."memberId" = m."memberId"
LEFT JOIN core."resultStatus" frs
  ON frs."statusId" = f."resultStatusId";

-- 8.4 Derived file stage.
-- DB-DD-003 requires this stage to be derived, but does not prescribe the exact code vocabulary.
-- The codes below are implementation labels only; no duplicate source-of-truth status column is stored.
CREATE OR REPLACE VIEW patient."vMemberFileStage" AS
WITH required AS (
    SELECT
        m."memberId",
        count(cc."clinicId")::integer AS "requiredCount"
    FROM patient."member" m
    LEFT JOIN core."centerClinic" cc
      ON cc."centerId" = m."currentMedicalCenterId"
     AND cc."isActive" = true
    LEFT JOIN core."clinic" c
      ON c."clinicId" = cc."clinicId"
     AND c."isActive" = true
     AND c."countsForFinal" = true
    WHERE c."clinicId" IS NOT NULL
    GROUP BY m."memberId"
),
assessed AS (
    SELECT
        a."memberId",
        count(DISTINCT a."clinicId")::integer AS "assessedCount"
    FROM clinical."clinicAssessment" a
    JOIN clinical."medicalExam" e
      ON e."examId" = a."examId"
     AND e."isCurrent" = true
    JOIN patient."member" m
      ON m."memberId" = a."memberId"
     AND m."currentMedicalCenterId" = a."centerId"
    JOIN core."centerClinic" cc
      ON cc."centerId" = a."centerId"
     AND cc."clinicId" = a."clinicId"
     AND cc."isActive" = true
    JOIN core."clinic" c
      ON c."clinicId" = a."clinicId"
     AND c."isActive" = true
     AND c."countsForFinal" = true
    GROUP BY a."memberId"
),
deferral AS (
    SELECT d."memberId", true AS "hasOpenDeferral"
    FROM clinical."deferredAction" d
    WHERE d."status" <> 'CLOSED'
    GROUP BY d."memberId"
)
SELECT
    m."memberId",
    CASE
        WHEN COALESCE(r."requiredCount", 0) > 0
         AND COALESCE(a."assessedCount", 0) = r."requiredCount"
        THEN 'COMPLETE'
        ELSE 'INCOMPLETE'
    END::text AS "clinicCompletionState",
    COALESCE(d."hasOpenDeferral", false) AS "hasOpenDeferral",
    rs."code"::text AS "finalStatus",
    (ar."archiveId" IS NOT NULL) AS "isArchived",
    CASE
        WHEN ar."archiveId" IS NOT NULL THEN 'ARCHIVED'
        WHEN COALESCE(d."hasOpenDeferral", false) THEN 'DEFERRED'
        WHEN fd."finalDecisionId" IS NOT NULL THEN 'FINAL_DECISION'
        WHEN COALESCE(r."requiredCount", 0) > 0
         AND COALESCE(a."assessedCount", 0) = r."requiredCount" THEN 'CLINICS_COMPLETE'
        ELSE 'IN_EXAMINATION'
    END::text AS "fileStage"
FROM patient."member" m
LEFT JOIN required r
  ON r."memberId" = m."memberId"
LEFT JOIN assessed a
  ON a."memberId" = m."memberId"
LEFT JOIN deferral d
  ON d."memberId" = m."memberId"
LEFT JOIN clinical."finalDecision" fd
  ON fd."memberId" = m."memberId"
LEFT JOIN core."resultStatus" rs
  ON rs."statusId" = fd."resultStatusId"
LEFT JOIN archive."memberArchive" ar
  ON ar."memberId" = m."memberId";

-- 8.6 Files requiring review/export/re-export
CREATE OR REPLACE VIEW export."vPendingRecruitmentExport" AS
SELECT
    m."memberId",
    m."fileNumber",
    m."recruitmentCenterId",
    rc."code" AS "recruitmentCenterCode",
    rc."nameAr" AS "recruitmentCenterNameAr",
    rs."code"::text AS "finalStatus",
    COALESCE(fd."rejectionReason", finalDeferral."deferReason") AS "finalReason",
    m."dataRevision",
    m."reviewedRevision",
    m."reviewedByUserId",
    m."reviewedAt",
    (m."reviewedRevision" IS DISTINCT FROM m."dataRevision") AS "requiresReview",
    m."lastExportedRevision",
    m."exportStatus",
    fd."decidedAt" AS "decisionAt"
FROM patient."member" m
JOIN clinical."finalDecision" fd
  ON fd."memberId" = m."memberId"
JOIN core."resultStatus" rs
  ON rs."statusId" = fd."resultStatusId"
JOIN core."recruitmentCenter" rc
  ON rc."recruitmentCenterId" = m."recruitmentCenterId"
LEFT JOIN LATERAL (
    SELECT d."deferReason"
    FROM clinical."deferredAction" d
    WHERE d."finalDecisionId" = fd."finalDecisionId"
      AND d."status" <> 'CLOSED'
    ORDER BY d."requestedAt" DESC
    LIMIT 1
) finalDeferral ON true
WHERE m."reviewedRevision" IS DISTINCT FROM m."dataRevision"
   OR m."exportStatus" = 'UNEXPORTED'
   OR m."lastExportedRevision" IS DISTINCT FROM m."dataRevision";

CREATE OR REPLACE VIEW patient."vMemberUnifiedFile" AS
SELECT
    m."memberId",
    m."fileNumber",
    m."recruitmentNumber",
    m."fullName",
    m."motherName",
    m."birthDate",
    m."maritalStatusId",
    m."occupation",
    m."bloodTypeId",
    m."bloodSugarMgDl",
    m."heightCm",
    m."weightKg",
    m."bmi",
    bmi."bmiCategoryId",
    bmi."bmiCategoryCode",
    bmi."bmiCategoryNameAr",
    m."bloodPressure",
    m."pulseBpm",
    m."hasTattoo",
    m."distinguishingMarks",
    stage."clinicCompletionState",
    stage."hasOpenDeferral",
    stage."finalStatus",
    stage."isArchived",
    stage."fileStage",
    m."currentMedicalCenterId",
    mc."code" AS "currentMedicalCenterCode",
    mc."nameAr" AS "currentMedicalCenterNameAr",
    currentIdentity."centerMemberId" AS "currentCenterMemberId",
    m."queueMedicalCenterId",
    m."queueDate",
    m."queueNumber",
    m."recruitmentCenterId",
    rc."code" AS "recruitmentCenterCode",
    rc."nameAr" AS "recruitmentCenterNameAr",
    m."exportStatus",
    m."dataRevision",
    m."lastExportedRevision",
    m."lastExportedAt",
    m."reviewedByUserId",
    m."reviewedAt",
    m."reviewedRevision",

    COALESCE((
        SELECT jsonb_agg(to_jsonb(i) ORDER BY i."validFrom")
        FROM patient."memberCenterIdentity" i
        WHERE i."memberId" = m."memberId"
    ), '[]'::jsonb) AS "localIdentityHistory",

    COALESCE((
        SELECT jsonb_agg(to_jsonb(t) ORDER BY t."executedAt")
        FROM patient."memberTransfer" t
        WHERE t."memberId" = m."memberId"
    ), '[]'::jsonb) AS "transferHistory",

    (
        SELECT to_jsonb(n)
        FROM patient."memberGeneralNote" n
        WHERE n."memberId" = m."memberId"
    ) AS "generalNote",

    COALESCE((
        SELECT jsonb_agg(
            to_jsonb(e)
            || jsonb_build_object(
                'clinicCode', c."code",
                'assessment', (
                    SELECT to_jsonb(a)
                    FROM clinical."clinicAssessment" a
                    WHERE a."examId" = e."examId"
                ),
                'eyeDetail', (
                    SELECT to_jsonb(ed)
                    FROM clinical."eyeExamDetail" ed
                    WHERE ed."examId" = e."examId"
                ),
                'eyeRefractions', COALESCE((
                    SELECT jsonb_agg(to_jsonb(er) ORDER BY er."eyeSide", er."refractionType")
                    FROM clinical."eyeRefractionDetail" er
                    WHERE er."examId" = e."examId"
                ), '[]'::jsonb),
                'internalDetail', (
                    SELECT to_jsonb(id)
                    FROM clinical."internalExamDetail" id
                    WHERE id."examId" = e."examId"
                ),
                'surgeryDetail', (
                    SELECT to_jsonb(sd)
                    FROM clinical."surgeryExamDetail" sd
                    WHERE sd."examId" = e."examId"
                ),
                'orthopedicDetail', (
                    SELECT to_jsonb(od)
                    FROM clinical."orthopedicExamDetail" od
                    WHERE od."examId" = e."examId"
                ),
                'entDetail', (
                    SELECT to_jsonb(nd)
                    FROM clinical."entExamDetail" nd
                    WHERE nd."examId" = e."examId"
                )
            )
            ORDER BY e."examinedAt", e."examSequence"
        )
        FROM clinical."medicalExam" e
        JOIN core."clinic" c ON c."clinicId" = e."clinicId"
        WHERE e."memberId" = m."memberId"
    ), '[]'::jsonb) AS "exams",

    COALESCE((
        SELECT jsonb_agg(to_jsonb(d) ORDER BY d."requestedAt")
        FROM clinical."deferredAction" d
        WHERE d."memberId" = m."memberId"
    ), '[]'::jsonb) AS "deferredActions",

    (
        SELECT to_jsonb(fd)
        FROM clinical."finalDecision" fd
        WHERE fd."memberId" = m."memberId"
    ) AS "finalDecision",

    COALESCE((
        SELECT jsonb_agg(to_jsonb(h) ORDER BY h."changedAt")
        FROM clinical."finalDecisionHistory" h
        WHERE h."memberId" = m."memberId"
    ), '[]'::jsonb) AS "finalDecisionHistory",

    COALESCE((
        SELECT jsonb_agg(to_jsonb(o) ORDER BY o."createdAt")
        FROM clinical."supervisorObjection" o
        WHERE o."memberId" = m."memberId"
    ), '[]'::jsonb) AS "supervisorObjections",

    COALESCE((
        SELECT jsonb_agg(to_jsonb(cn) ORDER BY cn."requestedAt")
        FROM medical."consultation" cn
        WHERE cn."memberId" = m."memberId"
    ), '[]'::jsonb) AS "consultations",

    COALESCE((
        SELECT jsonb_agg(to_jsonb(iv) ORDER BY iv."requestedAt")
        FROM medical."investigation" iv
        WHERE iv."memberId" = m."memberId"
    ), '[]'::jsonb) AS "investigations",

    COALESCE((
        SELECT jsonb_agg(
            to_jsonb(ma) || jsonb_build_object('fileAsset', to_jsonb(fa))
            ORDER BY ma."createdAt"
        )
        FROM medical."medicalAttachment" ma
        JOIN core."fileAsset" fa ON fa."fileAssetId" = ma."fileAssetId"
        WHERE ma."memberId" = m."memberId"
    ), '[]'::jsonb) AS "attachments",

    (
        SELECT to_jsonb(ar)
        FROM archive."memberArchive" ar
        WHERE ar."memberId" = m."memberId"
    ) AS "archive"
FROM patient."member" m
JOIN core."medicalCenter" mc
  ON mc."centerId" = m."currentMedicalCenterId"
JOIN core."recruitmentCenter" rc
  ON rc."recruitmentCenterId" = m."recruitmentCenterId"
LEFT JOIN patient."vMemberBmi" bmi
  ON bmi."memberId" = m."memberId"
LEFT JOIN patient."vMemberFileStage" stage
  ON stage."memberId" = m."memberId"
LEFT JOIN patient."memberCenterIdentity" currentIdentity
  ON currentIdentity."memberId" = m."memberId"
 AND currentIdentity."medicalCenterId" = m."currentMedicalCenterId"
 AND currentIdentity."validTo" IS NULL;

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
