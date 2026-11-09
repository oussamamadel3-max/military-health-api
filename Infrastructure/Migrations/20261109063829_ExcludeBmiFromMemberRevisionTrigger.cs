using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExcludeBmiFromMemberRevisionTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // قائمة صريحة بالأعمدة المادية؛ تتجاهل bmi المولَّد وحقول التصدير/المراجعة/التدقيق.
            migrationBuilder.Sql(
                """
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
                        NEW."fileNumber",
                        NEW."recruitmentNumber",
                        NEW."fullName",
                        NEW."motherName",
                        NEW."birthDate",
                        NEW."maritalStatusId",
                        NEW."occupation",
                        NEW."recruitmentCenterId",
                        NEW."bloodTypeId",
                        NEW."bloodSugarMgDl",
                        NEW."heightCm",
                        NEW."weightKg",
                        NEW."bloodPressure",
                        NEW."pulseBpm",
                        NEW."hasTattoo",
                        NEW."distinguishingMarks",
                        NEW."currentMedicalCenterId",
                        NEW."queueMedicalCenterId",
                        NEW."queueDate",
                        NEW."queueNumber"
                    ) IS DISTINCT FROM (
                        OLD."fileNumber",
                        OLD."recruitmentNumber",
                        OLD."fullName",
                        OLD."motherName",
                        OLD."birthDate",
                        OLD."maritalStatusId",
                        OLD."occupation",
                        OLD."recruitmentCenterId",
                        OLD."bloodTypeId",
                        OLD."bloodSugarMgDl",
                        OLD."heightCm",
                        OLD."weightKg",
                        OLD."bloodPressure",
                        OLD."pulseBpm",
                        OLD."hasTattoo",
                        OLD."distinguishingMarks",
                        OLD."currentMedicalCenterId",
                        OLD."queueMedicalCenterId",
                        OLD."queueDate",
                        OLD."queueNumber"
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // السلوك السابق: مقارنة to_jsonb مع استثناء حقول التصدير/المراجعة فقط (يشمل علة bmi).
            migrationBuilder.Sql(
                """
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
                """);
        }
    }
}
