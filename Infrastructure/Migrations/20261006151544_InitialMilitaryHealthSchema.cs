using System;
using System.Net;
using Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMilitaryHealthSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Extensions stay installed on Down. Other databases on this server may adopt them later.
            foreach (var statement in InitialDatabaseObjects.ExtensionStatements)
                migrationBuilder.Sql(statement);

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "clinical");

            migrationBuilder.EnsureSchema(
                name: "medical");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "iam");

            migrationBuilder.EnsureSchema(
                name: "patient");

            migrationBuilder.EnsureSchema(
                name: "archive");

            migrationBuilder.EnsureSchema(
                name: "export");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "bloodType",
                schema: "core",
                columns: table => new
                {
                    bloodTypeId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    sortOrder = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    isSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloodType", x => x.bloodTypeId);
                    table.CheckConstraint("ckBloodType1", "\"sortOrder\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "bmiCategory",
                schema: "core",
                columns: table => new
                {
                    categoryId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    minBmi = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    maxBmi = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    sortOrder = table.Column<short>(type: "smallint", nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bmiCategory", x => x.categoryId);
                    table.CheckConstraint("ckBmiCategory1", "\"minBmi\" >= 0 AND (\"maxBmi\" IS NULL OR \"maxBmi\" > \"minBmi\")");
                });

            migrationBuilder.CreateTable(
                name: "clinic",
                schema: "core",
                columns: table => new
                {
                    clinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    sortOrder = table.Column<short>(type: "smallint", nullable: false),
                    countsForFinal = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinic", x => x.clinicId);
                });

            migrationBuilder.CreateTable(
                name: "consultationType",
                schema: "core",
                columns: table => new
                {
                    consultationTypeId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    sortOrder = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    isSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consultationType", x => x.consultationTypeId);
                    table.CheckConstraint("ckConsultationType1", "\"sortOrder\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "FieldPermissions",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityName = table.Column<string>(type: "text", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    AccessType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "investigationType",
                schema: "core",
                columns: table => new
                {
                    investigationTypeId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    sortOrder = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    isSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investigationType", x => x.investigationTypeId);
                    table.CheckConstraint("ckInvestigationType1", "\"sortOrder\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "maritalStatus",
                schema: "core",
                columns: table => new
                {
                    maritalStatusId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    sortOrder = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    isSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maritalStatus", x => x.maritalStatusId);
                    table.CheckConstraint("ckMaritalStatus1", "\"sortOrder\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "medicalCenter",
                schema: "core",
                columns: table => new
                {
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    locationText = table.Column<string>(type: "text", nullable: true),
                    parentOrgText = table.Column<string>(type: "text", nullable: true),
                    contactInfo = table.Column<string>(type: "text", nullable: true),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deactivationReason = table.Column<string>(type: "text", nullable: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicalCenter", x => x.centerId);
                    table.CheckConstraint("ckMedicalCenterDeactivation", "(\"isActive\" = true AND \"deactivationReason\" IS NULL) OR (\"isActive\" = false AND \"deactivationReason\" IS NOT NULL AND btrim(\"deactivationReason\") <> '')");
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recruitmentCenter",
                schema: "core",
                columns: table => new
                {
                    recruitmentCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    nameEn = table.Column<string>(type: "text", nullable: true),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recruitmentCenter", x => x.recruitmentCenterId);
                });

            migrationBuilder.CreateTable(
                name: "requiredActionType",
                schema: "core",
                columns: table => new
                {
                    actionTypeId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    requiresDescription = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    requiresTargetClinic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requiredActionType", x => x.actionTypeId);
                });

            migrationBuilder.CreateTable(
                name: "resultStatus",
                schema: "core",
                columns: table => new
                {
                    statusId = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    isLocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resultStatus", x => x.statusId);
                    table.CheckConstraint("ckResultStatus1", "\"statusId\" IN (1,2,3)");
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true),
                    NormalizedName = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FullName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    LoginLockoutTier = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    NormalizedUserName = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    NormalizedEmail = table.Column<string>(type: "text", nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "centerClinic",
                schema: "core",
                columns: table => new
                {
                    centerClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    clinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    activatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_centerClinic", x => x.centerClinicId);
                    table.UniqueConstraint("AK_centerClinic_centerId_clinicId", x => new { x.centerId, x.clinicId });
                    table.CheckConstraint("ckCenterClinic1", "(\"isActive\" = true AND \"deactivatedAt\" IS NULL) OR (\"isActive\" = false AND \"deactivatedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_centerClinic_clinic_clinicId",
                        column: x => x.clinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_centerClinic_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoleFieldPermissions",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldPermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleFieldPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleFieldPermissions_FieldPermissions_FieldPermissionId",
                        column: x => x.FieldPermissionId,
                        principalSchema: "iam",
                        principalTable: "FieldPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleFieldPermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "iam",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event",
                schema: "audit",
                columns: table => new
                {
                    eventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actorUserId = table.Column<string>(type: "text", nullable: true),
                    contextScope = table.Column<string>(type: "citext", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: true),
                    eventType = table.Column<string>(type: "citext", nullable: false),
                    result = table.Column<string>(type: "citext", nullable: false),
                    entityType = table.Column<string>(type: "text", nullable: true),
                    entityId = table.Column<string>(type: "text", nullable: true),
                    reasonText = table.Column<string>(type: "text", nullable: true),
                    oldValues = table.Column<string>(type: "jsonb", nullable: true),
                    newValues = table.Column<string>(type: "jsonb", nullable: true),
                    ipAddress = table.Column<IPAddress>(type: "inet", nullable: true),
                    userAgent = table.Column<string>(type: "text", nullable: true),
                    correlationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event", x => x.eventId);
                    table.CheckConstraint("ckAuditEventContext", "(\"contextScope\" = 'CENTER' AND \"centerId\" IS NOT NULL) OR (\"contextScope\" = 'CENTRAL' AND \"centerId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_event_Users_actorUserId",
                        column: x => x.actorUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_event_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fileAsset",
                schema: "core",
                columns: table => new
                {
                    fileAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    storageKey = table.Column<string>(type: "text", nullable: false),
                    originalName = table.Column<string>(type: "text", nullable: false),
                    mimeType = table.Column<string>(type: "text", nullable: false),
                    extension = table.Column<string>(type: "citext", nullable: false),
                    sizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    uploadedByUserId = table.Column<string>(type: "text", nullable: false),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    isDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fileAsset", x => x.fileAssetId);
                    table.CheckConstraint("ckFileAsset1", "\"sizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_fileAsset_Users_uploadedByUserId",
                        column: x => x.uploadedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fileAsset_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "member",
                schema: "patient",
                columns: table => new
                {
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    fileNumber = table.Column<long>(type: "bigint", nullable: false),
                    recruitmentNumber = table.Column<string>(type: "citext", nullable: false),
                    fullName = table.Column<string>(type: "text", nullable: false),
                    motherName = table.Column<string>(type: "text", nullable: false),
                    birthDate = table.Column<DateOnly>(type: "date", nullable: false),
                    maritalStatusId = table.Column<short>(type: "smallint", nullable: false),
                    occupation = table.Column<string>(type: "text", nullable: false),
                    recruitmentCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    bloodTypeId = table.Column<short>(type: "smallint", nullable: true),
                    bloodSugarMgDl = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    heightCm = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    weightKg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    bmi = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true, computedColumnSql: "CASE WHEN \"heightCm\" IS NULL OR \"weightKg\" IS NULL THEN NULL ELSE round((\"weightKg\" / power((\"heightCm\" / 100.0), 2))::numeric, 1) END", stored: true),
                    bloodPressure = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    pulseBpm = table.Column<short>(type: "smallint", nullable: true),
                    hasTattoo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    distinguishingMarks = table.Column<string>(type: "text", nullable: true),
                    currentMedicalCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    queueMedicalCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    queueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    queueNumber = table.Column<int>(type: "integer", nullable: false),
                    exportStatus = table.Column<string>(type: "citext", nullable: false, defaultValue: "UNEXPORTED"),
                    dataRevision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    lastExportedRevision = table.Column<long>(type: "bigint", nullable: true),
                    lastExportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    createdByUserId = table.Column<string>(type: "text", nullable: false),
                    updatedByUserId = table.Column<string>(type: "text", nullable: false),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewedByUserId = table.Column<string>(type: "text", nullable: true),
                    reviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewedRevision = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member", x => x.memberId);
                    table.CheckConstraint("ckMember1", "\"bloodSugarMgDl\" IS NULL OR \"bloodSugarMgDl\" > 0");
                    table.CheckConstraint("ckMember2", "\"heightCm\" IS NULL OR \"heightCm\" > 0");
                    table.CheckConstraint("ckMember3", "\"weightKg\" IS NULL OR \"weightKg\" > 0");
                    table.CheckConstraint("ckMember4", "\"pulseBpm\" IS NULL OR \"pulseBpm\" > 0");
                    table.CheckConstraint("ckMember5", "\"queueNumber\" > 0");
                    table.CheckConstraint("ckMember6", "\"exportStatus\" IN ('UNEXPORTED','EXPORTED')");
                    table.CheckConstraint("ckMember7", "((\"reviewedByUserId\" IS NULL AND \"reviewedAt\" IS NULL AND \"reviewedRevision\" IS NULL) OR (\"reviewedByUserId\" IS NOT NULL AND \"reviewedAt\" IS NOT NULL AND \"reviewedRevision\" IS NOT NULL))");
                    table.CheckConstraint("ckMember8", "\"reviewedRevision\" IS NULL OR \"reviewedRevision\" <= \"dataRevision\"");
                    table.ForeignKey(
                        name: "FK_member_Users_createdByUserId",
                        column: x => x.createdByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_Users_reviewedByUserId",
                        column: x => x.reviewedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_Users_updatedByUserId",
                        column: x => x.updatedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_bloodType_bloodTypeId",
                        column: x => x.bloodTypeId,
                        principalSchema: "core",
                        principalTable: "bloodType",
                        principalColumn: "bloodTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_maritalStatus_maritalStatusId",
                        column: x => x.maritalStatusId,
                        principalSchema: "core",
                        principalTable: "maritalStatus",
                        principalColumn: "maritalStatusId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_medicalCenter_currentMedicalCenterId",
                        column: x => x.currentMedicalCenterId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_medicalCenter_queueMedicalCenterId",
                        column: x => x.queueMedicalCenterId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_recruitmentCenter_recruitmentCenterId",
                        column: x => x.recruitmentCenterId,
                        principalSchema: "core",
                        principalTable: "recruitmentCenter",
                        principalColumn: "recruitmentCenterId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "passwordPolicy",
                schema: "iam",
                columns: table => new
                {
                    passwordPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    versionNo = table.Column<int>(type: "integer", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    minLength = table.Column<short>(type: "smallint", nullable: false),
                    maxLength = table.Column<short>(type: "smallint", nullable: false),
                    requireUppercase = table.Column<bool>(type: "boolean", nullable: false),
                    requireLowercase = table.Column<bool>(type: "boolean", nullable: false),
                    requireDigit = table.Column<bool>(type: "boolean", nullable: false),
                    requireSpecial = table.Column<bool>(type: "boolean", nullable: false),
                    historyDepth = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    validFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    updatedByUserId = table.Column<string>(type: "text", nullable: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passwordPolicy", x => x.passwordPolicyId);
                    table.CheckConstraint("ckPasswordPolicy1", "\"validTo\" IS NULL OR \"validTo\" > \"validFrom\"");
                    table.CheckConstraint("ckPasswordPolicy2", "\"versionNo\" > 0");
                    table.CheckConstraint("ckPasswordPolicy3", "\"minLength\" > 0");
                    table.CheckConstraint("ckPasswordPolicy4", "\"historyDepth\" >= 0");
                    table.CheckConstraint("ckPasswordPolicyLengthRange", "\"maxLength\" >= \"minLength\"");
                    table.CheckConstraint("ckPasswordPolicyMaxLengthPositive", "\"maxLength\" > 0");
                    table.ForeignKey(
                        name: "FK_passwordPolicy_Users_updatedByUserId",
                        column: x => x.updatedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refreshToken",
                schema: "iam",
                columns: table => new
                {
                    refreshTokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    userId = table.Column<string>(type: "text", nullable: false),
                    tokenHash = table.Column<string>(type: "text", nullable: false),
                    tokenFamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    parentRefreshTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    securityStamp = table.Column<string>(type: "text", nullable: true),
                    issuedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    isRevoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    revokedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    revokeReason = table.Column<string>(type: "text", nullable: true),
                    replacedByRefreshTokenId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refreshToken", x => x.refreshTokenId);
                    table.ForeignKey(
                        name: "FK_refreshToken_Users_userId",
                        column: x => x.userId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_refreshToken_refreshToken_parentRefreshTokenId",
                        column: x => x.parentRefreshTokenId,
                        principalSchema: "iam",
                        principalTable: "refreshToken",
                        principalColumn: "refreshTokenId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refreshToken_refreshToken_replacedByRefreshTokenId",
                        column: x => x.replacedByRefreshTokenId,
                        principalSchema: "iam",
                        principalTable: "refreshToken",
                        principalColumn: "refreshTokenId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    GrantedByUserId = table.Column<string>(type: "text", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedByUserId = table.Column<string>(type: "text", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokeReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.CheckConstraint("ckRolePermission1", "(\"IsActive\" = true AND \"RevokedAt\" IS NULL AND \"RevokedByUserId\" IS NULL AND (\"RevokeReason\" IS NULL OR btrim(\"RevokeReason\") = '')) OR (\"IsActive\" = false AND \"RevokedAt\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL AND \"RevokeReason\" IS NOT NULL AND btrim(\"RevokeReason\") <> '')");
                    table.CheckConstraint("ckRolePermission2", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"GrantedAt\"");
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalSchema: "iam",
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "iam",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "userCenter",
                schema: "iam",
                columns: table => new
                {
                    userId = table.Column<string>(type: "text", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    assignedByUserId = table.Column<string>(type: "text", nullable: false),
                    assignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revokedByUserId = table.Column<string>(type: "text", nullable: true),
                    revokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revokeReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userCenter", x => new { x.userId, x.centerId });
                    table.CheckConstraint("ckUserCenter1", "(\"isActive\" = true AND \"revokedAt\" IS NULL AND \"revokedByUserId\" IS NULL AND (\"revokeReason\" IS NULL OR btrim(\"revokeReason\") = '')) OR (\"isActive\" = false AND \"revokedAt\" IS NOT NULL AND \"revokedByUserId\" IS NOT NULL AND \"revokeReason\" IS NOT NULL AND btrim(\"revokeReason\") <> '')");
                    table.CheckConstraint("ckUserCenter2", "\"revokedAt\" IS NULL OR \"revokedAt\" >= \"assignedAt\"");
                    table.ForeignKey(
                        name: "FK_userCenter_Users_assignedByUserId",
                        column: x => x.assignedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userCenter_Users_revokedByUserId",
                        column: x => x.revokedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userCenter_Users_userId",
                        column: x => x.userId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userCenter_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "userContact",
                schema: "iam",
                columns: table => new
                {
                    contactId = table.Column<Guid>(type: "uuid", nullable: false),
                    userId = table.Column<string>(type: "text", nullable: false),
                    contactType = table.Column<string>(type: "citext", nullable: false),
                    contactValue = table.Column<string>(type: "text", nullable: false),
                    isPrimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userContact", x => x.contactId);
                    table.CheckConstraint("ckUserContact1", "\"contactType\" IN ('PHONE','EMAIL','OTHER')");
                    table.CheckConstraint("ckUserContact2", "btrim(\"contactValue\") <> ''");
                    table.ForeignKey(
                        name: "FK_userContact_Users_userId",
                        column: x => x.userId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserFieldPermissions",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldPermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFieldPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFieldPermissions_FieldPermissions_FieldPermissionId",
                        column: x => x.FieldPermissionId,
                        principalSchema: "iam",
                        principalTable: "FieldPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFieldPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CenterId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    GrantedByUserId = table.Column<string>(type: "text", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedByUserId = table.Column<string>(type: "text", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokeReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => x.Id);
                    table.CheckConstraint("ckUserPermissionAssignment1", "(\"IsActive\" = true AND \"RevokedAt\" IS NULL AND \"RevokedByUserId\" IS NULL AND (\"RevokeReason\" IS NULL OR btrim(\"RevokeReason\") = '')) OR (\"IsActive\" = false AND \"RevokedAt\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL AND \"RevokeReason\" IS NOT NULL AND btrim(\"RevokeReason\") <> '')");
                    table.CheckConstraint("ckUserPermissionAssignment2", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"GrantedAt\"");
                    table.ForeignKey(
                        name: "FK_UserPermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalSchema: "iam",
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPermissions_medicalCenter_CenterId",
                        column: x => x.CenterId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserRoleAssociations",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    CenterId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AssignedByUserId = table.Column<string>(type: "text", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedByUserId = table.Column<string>(type: "text", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokeReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoleAssociations", x => x.Id);
                    table.CheckConstraint("ckUserRoleAssignment1", "(\"IsActive\" = true AND \"RevokedAt\" IS NULL AND \"RevokedByUserId\" IS NULL AND (\"RevokeReason\" IS NULL OR btrim(\"RevokeReason\") = '')) OR (\"IsActive\" = false AND \"RevokedAt\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL AND \"RevokeReason\" IS NOT NULL AND btrim(\"RevokeReason\") <> '')");
                    table.CheckConstraint("ckUserRoleAssignment2", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"AssignedAt\"");
                    table.ForeignKey(
                        name: "FK_UserRoleAssociations_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "iam",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoleAssociations_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssociations_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssociations_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoleAssociations_medicalCenter_CenterId",
                        column: x => x.CenterId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "validationRule",
                schema: "core",
                columns: table => new
                {
                    validationRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", nullable: false),
                    nameAr = table.Column<string>(type: "text", nullable: false),
                    targetEntity = table.Column<string>(type: "text", nullable: false),
                    targetField = table.Column<string>(type: "text", nullable: false),
                    minValue = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    maxValue = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "citext", nullable: true),
                    versionNo = table.Column<int>(type: "integer", nullable: false),
                    validFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    updatedByUserId = table.Column<string>(type: "text", nullable: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_validationRule", x => x.validationRuleId);
                    table.CheckConstraint("ckValidationRule1", "\"minValue\" IS NOT NULL OR \"maxValue\" IS NOT NULL");
                    table.CheckConstraint("ckValidationRule2", "\"minValue\" IS NULL OR \"maxValue\" IS NULL OR \"maxValue\" >= \"minValue\"");
                    table.CheckConstraint("ckValidationRule3", "\"validTo\" IS NULL OR \"validTo\" > \"validFrom\"");
                    table.CheckConstraint("ckValidationRule4", "\"versionNo\" > 0");
                    table.ForeignKey(
                        name: "FK_validationRule_Users_updatedByUserId",
                        column: x => x.updatedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crossCenterAccess",
                schema: "audit",
                columns: table => new
                {
                    accessId = table.Column<Guid>(type: "uuid", nullable: false),
                    auditEventId = table.Column<long>(type: "bigint", nullable: false),
                    targetCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    accessReason = table.Column<string>(type: "text", nullable: false),
                    dataType = table.Column<string>(type: "text", nullable: false),
                    queryCriteria = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crossCenterAccess", x => x.accessId);
                    table.CheckConstraint("ckCrossCenterAccess1", "btrim(\"accessReason\") <> ''");
                    table.ForeignKey(
                        name: "FK_crossCenterAccess_event_auditEventId",
                        column: x => x.auditEventId,
                        principalSchema: "audit",
                        principalTable: "event",
                        principalColumn: "eventId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_crossCenterAccess_medicalCenter_targetCenterId",
                        column: x => x.targetCenterId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recruitmentExportBatch",
                schema: "export",
                columns: table => new
                {
                    exportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerCodeSnapshot = table.Column<string>(type: "citext", nullable: false),
                    centerNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    exportedByUserId = table.Column<string>(type: "text", nullable: false),
                    generatedFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    recordCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "citext", nullable: false, defaultValue: "GENERATING"),
                    filterSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recruitmentExportBatch", x => x.exportBatchId);
                    table.CheckConstraint("ckRecruitmentExportBatch1", "\"recordCount\" >= 0");
                    table.CheckConstraint("ckRecruitmentExportBatch2", "\"status\" IN ('GENERATING','COMPLETED','FAILED')");
                    table.CheckConstraint("ckRecruitmentExportBatch3", "(\"status\" = 'COMPLETED' AND \"generatedFileId\" IS NOT NULL AND \"completedAt\" IS NOT NULL) OR (\"status\" = 'GENERATING' AND \"completedAt\" IS NULL) OR (\"status\" = 'FAILED' AND \"completedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_recruitmentExportBatch_Users_exportedByUserId",
                        column: x => x.exportedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recruitmentExportBatch_fileAsset_generatedFileId",
                        column: x => x.generatedFileId,
                        principalSchema: "core",
                        principalTable: "fileAsset",
                        principalColumn: "fileAssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recruitmentExportBatch_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "userProfile",
                schema: "iam",
                columns: table => new
                {
                    userId = table.Column<string>(type: "text", nullable: false),
                    employeeNumber = table.Column<string>(type: "citext", nullable: false),
                    rankOrTitle = table.Column<string>(type: "text", nullable: false),
                    updatedByUserId = table.Column<string>(type: "text", nullable: false),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    profileFileAssetId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userProfile", x => x.userId);
                    table.CheckConstraint("ckUserProfile1", "btrim(\"rankOrTitle\") <> ''");
                    table.ForeignKey(
                        name: "FK_userProfile_Users_updatedByUserId",
                        column: x => x.updatedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userProfile_Users_userId",
                        column: x => x.userId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userProfile_fileAsset_profileFileAssetId",
                        column: x => x.profileFileAssetId,
                        principalSchema: "core",
                        principalTable: "fileAsset",
                        principalColumn: "fileAssetId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consultation",
                schema: "medical",
                columns: table => new
                {
                    consultationId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    clinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    requestedByUserId = table.Column<string>(type: "text", nullable: false),
                    consultantUserId = table.Column<string>(type: "text", nullable: true),
                    consultantName = table.Column<string>(type: "text", nullable: true),
                    consultationTypeId = table.Column<short>(type: "smallint", nullable: false),
                    referralReason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "citext", nullable: false, defaultValue: "PENDING"),
                    resultText = table.Column<string>(type: "text", nullable: true),
                    requestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resultedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consultation", x => x.consultationId);
                    table.UniqueConstraint("AK_consultation_consultationId_memberId_centerId", x => new { x.consultationId, x.memberId, x.centerId });
                    table.CheckConstraint("ckConsultation1", "\"status\" IN ('PENDING','COMPLETED','CANCELLED')");
                    table.CheckConstraint("ckConsultation2", "(\"status\" = 'COMPLETED' AND \"resultText\" IS NOT NULL AND btrim(\"resultText\") <> '' AND \"resultedAt\" IS NOT NULL) OR (\"status\" IN ('PENDING','CANCELLED') AND \"resultText\" IS NULL AND \"resultedAt\" IS NULL)");
                    table.CheckConstraint("ckConsultation4", "\"resultedAt\" IS NULL OR \"resultedAt\" >= \"requestedAt\"");
                    table.ForeignKey(
                        name: "FK_consultation_Users_consultantUserId",
                        column: x => x.consultantUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consultation_Users_requestedByUserId",
                        column: x => x.requestedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consultation_clinic_clinicId",
                        column: x => x.clinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consultation_consultationType_consultationTypeId",
                        column: x => x.consultationTypeId,
                        principalSchema: "core",
                        principalTable: "consultationType",
                        principalColumn: "consultationTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consultation_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consultation_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finalDecision",
                schema: "clinical",
                columns: table => new
                {
                    finalDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    supervisorUserId = table.Column<string>(type: "text", nullable: false),
                    resultStatusId = table.Column<short>(type: "smallint", nullable: false),
                    rejectionReason = table.Column<string>(type: "text", nullable: true),
                    disagreementJustification = table.Column<string>(type: "text", nullable: true),
                    decidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedByUserId = table.Column<string>(type: "text", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finalDecision", x => x.finalDecisionId);
                    table.UniqueConstraint("AK_finalDecision_finalDecisionId_memberId", x => new { x.finalDecisionId, x.memberId });
                    table.UniqueConstraint("AK_finalDecision_finalDecisionId_memberId_centerId", x => new { x.finalDecisionId, x.memberId, x.centerId });
                    table.CheckConstraint("ckFinalDecision1", "(\"resultStatusId\" = 2 AND \"rejectionReason\" IS NOT NULL AND btrim(\"rejectionReason\") <> '') OR (\"resultStatusId\" <> 2 AND \"rejectionReason\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_finalDecision_Users_supervisorUserId",
                        column: x => x.supervisorUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finalDecision_Users_updatedByUserId",
                        column: x => x.updatedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finalDecision_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finalDecision_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finalDecision_resultStatus_resultStatusId",
                        column: x => x.resultStatusId,
                        principalSchema: "core",
                        principalTable: "resultStatus",
                        principalColumn: "statusId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "investigation",
                schema: "medical",
                columns: table => new
                {
                    investigationId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    clinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    requestedByUserId = table.Column<string>(type: "text", nullable: false),
                    investigationTypeId = table.Column<short>(type: "smallint", nullable: false),
                    testName = table.Column<string>(type: "text", nullable: false),
                    requestReason = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "citext", nullable: false, defaultValue: "PENDING"),
                    resultText = table.Column<string>(type: "text", nullable: true),
                    requestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resultedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investigation", x => x.investigationId);
                    table.UniqueConstraint("AK_investigation_investigationId_memberId_centerId", x => new { x.investigationId, x.memberId, x.centerId });
                    table.CheckConstraint("ckInvestigation1", "\"status\" IN ('PENDING','COMPLETED','CANCELLED')");
                    table.CheckConstraint("ckInvestigation2", "(\"status\" = 'COMPLETED' AND \"resultText\" IS NOT NULL AND btrim(\"resultText\") <> '' AND \"resultedAt\" IS NOT NULL) OR (\"status\" IN ('PENDING','CANCELLED') AND \"resultText\" IS NULL AND \"resultedAt\" IS NULL)");
                    table.CheckConstraint("ckInvestigation3", "\"resultedAt\" IS NULL OR \"resultedAt\" >= \"requestedAt\"");
                    table.ForeignKey(
                        name: "FK_investigation_Users_requestedByUserId",
                        column: x => x.requestedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investigation_clinic_clinicId",
                        column: x => x.clinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investigation_investigationType_investigationTypeId",
                        column: x => x.investigationTypeId,
                        principalSchema: "core",
                        principalTable: "investigationType",
                        principalColumn: "investigationTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investigation_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investigation_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medicalExam",
                schema: "clinical",
                columns: table => new
                {
                    examId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    clinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    examinerUserId = table.Column<string>(type: "text", nullable: false),
                    examinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    examSequence = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    isCurrent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    versionNo = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    updatedByUserId = table.Column<string>(type: "text", nullable: false),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicalExam", x => x.examId);
                    table.UniqueConstraint("AK_medicalExam_examId_memberId_centerId_clinicId", x => new { x.examId, x.memberId, x.centerId, x.clinicId });
                    table.CheckConstraint("ckMedicalExam1", "\"examSequence\" > 0");
                    table.CheckConstraint("ckMedicalExam2", "\"versionNo\" > 0");
                    table.ForeignKey(
                        name: "FK_medicalExam_Users_examinerUserId",
                        column: x => x.examinerUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicalExam_Users_updatedByUserId",
                        column: x => x.updatedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicalExam_clinic_clinicId",
                        column: x => x.clinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicalExam_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicalExam_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberCenterIdentity",
                schema: "patient",
                columns: table => new
                {
                    memberCenterIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    medicalCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerMemberId = table.Column<string>(type: "citext", nullable: false),
                    validFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assignedByUserId = table.Column<string>(type: "text", nullable: false),
                    closedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberCenterIdentity", x => x.memberCenterIdentityId);
                    table.UniqueConstraint("AK_memberCenterIdentity_memberCenterIdentityId_memberId_medica~", x => new { x.memberCenterIdentityId, x.memberId, x.medicalCenterId });
                    table.CheckConstraint("ckMemberCenterIdentity2", "\"validTo\" IS NULL OR \"validTo\" >= \"validFrom\"");
                    table.CheckConstraint("ckMemberCenterIdentity3", "((\"validTo\" IS NULL AND \"closedByUserId\" IS NULL) OR (\"validTo\" IS NOT NULL AND \"closedByUserId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_memberCenterIdentity_Users_assignedByUserId",
                        column: x => x.assignedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberCenterIdentity_Users_closedByUserId",
                        column: x => x.closedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberCenterIdentity_medicalCenter_medicalCenterId",
                        column: x => x.medicalCenterId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberCenterIdentity_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberGeneralNote",
                schema: "patient",
                columns: table => new
                {
                    noteId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    noteText = table.Column<string>(type: "text", nullable: false),
                    authorUserId = table.Column<string>(type: "text", nullable: false),
                    authorClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    writtenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedByUserId = table.Column<string>(type: "text", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberGeneralNote", x => x.noteId);
                    table.ForeignKey(
                        name: "FK_memberGeneralNote_Users_authorUserId",
                        column: x => x.authorUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberGeneralNote_Users_updatedByUserId",
                        column: x => x.updatedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberGeneralNote_clinic_authorClinicId",
                        column: x => x.authorClinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberGeneralNote_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberGeneralNote_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "userPasswordHistory",
                schema: "iam",
                columns: table => new
                {
                    passwordHistoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    userId = table.Column<string>(type: "text", nullable: false),
                    passwordHash = table.Column<string>(type: "text", nullable: false),
                    passwordPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    setAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userPasswordHistory", x => x.passwordHistoryId);
                    table.CheckConstraint("ckUserPasswordHistory1", "btrim(\"passwordHash\") <> ''");
                    table.ForeignKey(
                        name: "FK_userPasswordHistory_Users_userId",
                        column: x => x.userId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userPasswordHistory_passwordPolicy_passwordPolicyId",
                        column: x => x.passwordPolicyId,
                        principalSchema: "iam",
                        principalTable: "passwordPolicy",
                        principalColumn: "passwordPolicyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "userClinicAssignment",
                schema: "iam",
                columns: table => new
                {
                    assignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    userId = table.Column<string>(type: "text", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    clinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    specialtyTitle = table.Column<string>(type: "text", nullable: true),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    assignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assignedByUserId = table.Column<string>(type: "text", nullable: false),
                    revokedByUserId = table.Column<string>(type: "text", nullable: true),
                    revokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revokeReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userClinicAssignment", x => x.assignmentId);
                    table.CheckConstraint("ckUserClinicAssignment1", "(\"isActive\" = true AND \"revokedAt\" IS NULL AND \"revokedByUserId\" IS NULL AND (\"revokeReason\" IS NULL OR btrim(\"revokeReason\") = '')) OR (\"isActive\" = false AND \"revokedAt\" IS NOT NULL AND \"revokedByUserId\" IS NOT NULL AND \"revokeReason\" IS NOT NULL AND btrim(\"revokeReason\") <> '')");
                    table.CheckConstraint("ckUserClinicAssignment2", "\"revokedAt\" IS NULL OR \"revokedAt\" >= \"assignedAt\"");
                    table.ForeignKey(
                        name: "FK_userClinicAssignment_Users_assignedByUserId",
                        column: x => x.assignedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userClinicAssignment_Users_revokedByUserId",
                        column: x => x.revokedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userClinicAssignment_Users_userId",
                        column: x => x.userId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userClinicAssignment_centerClinic_centerId_clinicId",
                        columns: x => new { x.centerId, x.clinicId },
                        principalSchema: "core",
                        principalTable: "centerClinic",
                        principalColumns: new[] { "centerId", "clinicId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userClinicAssignment_clinic_clinicId",
                        column: x => x.clinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userClinicAssignment_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_userClinicAssignment_userCenter_userId_centerId",
                        columns: x => new { x.userId, x.centerId },
                        principalSchema: "iam",
                        principalTable: "userCenter",
                        principalColumns: new[] { "userId", "centerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finalDecisionHistory",
                schema: "clinical",
                columns: table => new
                {
                    historyId = table.Column<Guid>(type: "uuid", nullable: false),
                    finalDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    oldStatusId = table.Column<short>(type: "smallint", nullable: false),
                    newStatusId = table.Column<short>(type: "smallint", nullable: false),
                    changeReason = table.Column<string>(type: "text", nullable: false),
                    changedByUserId = table.Column<string>(type: "text", nullable: false),
                    changedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    exportResetPerformed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finalDecisionHistory", x => x.historyId);
                    table.CheckConstraint("ckFinalDecisionHistory1", "\"oldStatusId\" <> \"newStatusId\"");
                    table.ForeignKey(
                        name: "FK_finalDecisionHistory_Users_changedByUserId",
                        column: x => x.changedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finalDecisionHistory_finalDecision_finalDecisionId_memberId~",
                        columns: x => new { x.finalDecisionId, x.memberId, x.centerId },
                        principalSchema: "clinical",
                        principalTable: "finalDecision",
                        principalColumns: new[] { "finalDecisionId", "memberId", "centerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finalDecisionHistory_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finalDecisionHistory_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finalDecisionHistory_resultStatus_newStatusId",
                        column: x => x.newStatusId,
                        principalSchema: "core",
                        principalTable: "resultStatus",
                        principalColumn: "statusId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finalDecisionHistory_resultStatus_oldStatusId",
                        column: x => x.oldStatusId,
                        principalSchema: "core",
                        principalTable: "resultStatus",
                        principalColumn: "statusId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberArchive",
                schema: "archive",
                columns: table => new
                {
                    archiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    finalDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    archiveNumber = table.Column<string>(type: "citext", nullable: false),
                    archivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    digitalCopyFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberArchive", x => x.archiveId);
                    table.ForeignKey(
                        name: "FK_memberArchive_fileAsset_digitalCopyFileId",
                        column: x => x.digitalCopyFileId,
                        principalSchema: "core",
                        principalTable: "fileAsset",
                        principalColumn: "fileAssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberArchive_finalDecision_finalDecisionId_memberId_center~",
                        columns: x => new { x.finalDecisionId, x.memberId, x.centerId },
                        principalSchema: "clinical",
                        principalTable: "finalDecision",
                        principalColumns: new[] { "finalDecisionId", "memberId", "centerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberArchive_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberArchive_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recruitmentExportItem",
                schema: "export",
                columns: table => new
                {
                    exportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    finalDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    decisionAtSnapshot = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resultStatusId = table.Column<short>(type: "smallint", nullable: false),
                    resultReasonSnapshot = table.Column<string>(type: "text", nullable: true),
                    fileNumberSnapshot = table.Column<long>(type: "bigint", nullable: false),
                    recruitmentNumberSnapshot = table.Column<string>(type: "citext", nullable: false),
                    dataRevisionSnapshot = table.Column<long>(type: "bigint", nullable: false),
                    isReexport = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    previousExportBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    exportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recruitmentCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    recruitmentCenterCodeSnapshot = table.Column<string>(type: "citext", nullable: false),
                    recruitmentCenterNameSnapshot = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recruitmentExportItem", x => new { x.exportBatchId, x.memberId });
                    table.CheckConstraint("ckRecruitmentExportItem1", "(\"resultStatusId\" = 1 OR (\"resultStatusId\" IN (2,3) AND \"resultReasonSnapshot\" IS NOT NULL AND btrim(\"resultReasonSnapshot\") <> ''))");
                    table.CheckConstraint("ckRecruitmentExportItem2", "((\"isReexport\" = false AND \"previousExportBatchId\" IS NULL) OR (\"isReexport\" = true AND \"previousExportBatchId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_recruitmentExportItem_finalDecision_finalDecisionId_memberId",
                        columns: x => new { x.finalDecisionId, x.memberId },
                        principalSchema: "clinical",
                        principalTable: "finalDecision",
                        principalColumns: new[] { "finalDecisionId", "memberId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recruitmentExportItem_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recruitmentExportItem_recruitmentCenter_recruitmentCenterId",
                        column: x => x.recruitmentCenterId,
                        principalSchema: "core",
                        principalTable: "recruitmentCenter",
                        principalColumn: "recruitmentCenterId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recruitmentExportItem_recruitmentExportBatch_exportBatchId",
                        column: x => x.exportBatchId,
                        principalSchema: "export",
                        principalTable: "recruitmentExportBatch",
                        principalColumn: "exportBatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recruitmentExportItem_recruitmentExportItem_previousExportB~",
                        columns: x => new { x.previousExportBatchId, x.memberId },
                        principalSchema: "export",
                        principalTable: "recruitmentExportItem",
                        principalColumns: new[] { "exportBatchId", "memberId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recruitmentExportItem_resultStatus_resultStatusId",
                        column: x => x.resultStatusId,
                        principalSchema: "core",
                        principalTable: "resultStatus",
                        principalColumn: "statusId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medicalAttachment",
                schema: "medical",
                columns: table => new
                {
                    attachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    fileAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    consultationId = table.Column<Guid>(type: "uuid", nullable: true),
                    investigationId = table.Column<Guid>(type: "uuid", nullable: true),
                    uploadedByUserId = table.Column<string>(type: "text", nullable: false),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicalAttachment", x => x.attachmentId);
                    table.CheckConstraint("ckMedicalAttachment1", "(\"consultationId\" IS NOT NULL) <> (\"investigationId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_medicalAttachment_Users_uploadedByUserId",
                        column: x => x.uploadedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicalAttachment_consultation_consultationId_memberId_cent~",
                        columns: x => new { x.consultationId, x.memberId, x.centerId },
                        principalSchema: "medical",
                        principalTable: "consultation",
                        principalColumns: new[] { "consultationId", "memberId", "centerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicalAttachment_fileAsset_fileAssetId",
                        column: x => x.fileAssetId,
                        principalSchema: "core",
                        principalTable: "fileAsset",
                        principalColumn: "fileAssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicalAttachment_investigation_investigationId_memberId_ce~",
                        columns: x => new { x.investigationId, x.memberId, x.centerId },
                        principalSchema: "medical",
                        principalTable: "investigation",
                        principalColumns: new[] { "investigationId", "memberId", "centerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicalAttachment_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medicalAttachment_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clinicAssessment",
                schema: "clinical",
                columns: table => new
                {
                    assessmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    examId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    clinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    resultStatusId = table.Column<short>(type: "smallint", nullable: false),
                    rejectionReason = table.Column<string>(type: "text", nullable: true),
                    clinicalNotes = table.Column<string>(type: "text", nullable: true),
                    assessedByUserId = table.Column<string>(type: "text", nullable: false),
                    assessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedByUserId = table.Column<string>(type: "text", nullable: false),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinicAssessment", x => x.assessmentId);
                    table.UniqueConstraint("AK_clinicAssessment_assessmentId_memberId_centerId", x => new { x.assessmentId, x.memberId, x.centerId });
                    table.UniqueConstraint("AK_clinicAssessment_assessmentId_memberId_centerId_clinicId", x => new { x.assessmentId, x.memberId, x.centerId, x.clinicId });
                    table.CheckConstraint("ckClinicAssessment1", "(\"resultStatusId\" = 2 AND \"rejectionReason\" IS NOT NULL AND btrim(\"rejectionReason\") <> '') OR (\"resultStatusId\" <> 2 AND \"rejectionReason\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_clinicAssessment_Users_assessedByUserId",
                        column: x => x.assessedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinicAssessment_Users_updatedByUserId",
                        column: x => x.updatedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinicAssessment_clinic_clinicId",
                        column: x => x.clinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinicAssessment_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinicAssessment_medicalExam_examId_memberId_centerId_clini~",
                        columns: x => new { x.examId, x.memberId, x.centerId, x.clinicId },
                        principalSchema: "clinical",
                        principalTable: "medicalExam",
                        principalColumns: new[] { "examId", "memberId", "centerId", "clinicId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinicAssessment_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinicAssessment_resultStatus_resultStatusId",
                        column: x => x.resultStatusId,
                        principalSchema: "core",
                        principalTable: "resultStatus",
                        principalColumn: "statusId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entExamDetail",
                schema: "clinical",
                columns: table => new
                {
                    examId = table.Column<Guid>(type: "uuid", nullable: false),
                    rightEar = table.Column<string>(type: "text", nullable: true),
                    leftEar = table.Column<string>(type: "text", nullable: true),
                    tympanicMembrane = table.Column<string>(type: "text", nullable: true),
                    hearingTest = table.Column<string>(type: "text", nullable: true),
                    whisperTest = table.Column<string>(type: "text", nullable: true),
                    noseExam = table.Column<string>(type: "text", nullable: true),
                    mouthExam = table.Column<string>(type: "text", nullable: true),
                    otherEntDiseases = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entExamDetail", x => x.examId);
                    table.ForeignKey(
                        name: "FK_entExamDetail_medicalExam_examId",
                        column: x => x.examId,
                        principalSchema: "clinical",
                        principalTable: "medicalExam",
                        principalColumn: "examId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eyeExamDetail",
                schema: "clinical",
                columns: table => new
                {
                    examId = table.Column<Guid>(type: "uuid", nullable: false),
                    visualAcuityRight = table.Column<string>(type: "text", nullable: true),
                    visualAcuityLeft = table.Column<string>(type: "text", nullable: true),
                    colorTestResult = table.Column<string>(type: "text", nullable: true),
                    otherEyeDiseases = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eyeExamDetail", x => x.examId);
                    table.ForeignKey(
                        name: "FK_eyeExamDetail_medicalExam_examId",
                        column: x => x.examId,
                        principalSchema: "clinical",
                        principalTable: "medicalExam",
                        principalColumn: "examId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "internalExamDetail",
                schema: "clinical",
                columns: table => new
                {
                    examId = table.Column<Guid>(type: "uuid", nullable: false),
                    cardiovascular = table.Column<string>(type: "text", nullable: true),
                    respiratory = table.Column<string>(type: "text", nullable: true),
                    digestive = table.Column<string>(type: "text", nullable: true),
                    endocrine = table.Column<string>(type: "text", nullable: true),
                    neurological = table.Column<string>(type: "text", nullable: true),
                    hematology = table.Column<string>(type: "text", nullable: true),
                    joints = table.Column<string>(type: "text", nullable: true),
                    renal = table.Column<string>(type: "text", nullable: true),
                    skin = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_internalExamDetail", x => x.examId);
                    table.ForeignKey(
                        name: "FK_internalExamDetail_medicalExam_examId",
                        column: x => x.examId,
                        principalSchema: "clinical",
                        principalTable: "medicalExam",
                        principalColumn: "examId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "orthopedicExamDetail",
                schema: "clinical",
                columns: table => new
                {
                    examId = table.Column<Guid>(type: "uuid", nullable: false),
                    musculoskeletal = table.Column<string>(type: "text", nullable: true),
                    neurosurgery = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orthopedicExamDetail", x => x.examId);
                    table.ForeignKey(
                        name: "FK_orthopedicExamDetail_medicalExam_examId",
                        column: x => x.examId,
                        principalSchema: "clinical",
                        principalTable: "medicalExam",
                        principalColumn: "examId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "surgeryExamDetail",
                schema: "clinical",
                columns: table => new
                {
                    examId = table.Column<Guid>(type: "uuid", nullable: false),
                    generalSurgery = table.Column<string>(type: "text", nullable: true),
                    urology = table.Column<string>(type: "text", nullable: true),
                    vascularThoracic = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_surgeryExamDetail", x => x.examId);
                    table.ForeignKey(
                        name: "FK_surgeryExamDetail_medicalExam_examId",
                        column: x => x.examId,
                        principalSchema: "clinical",
                        principalTable: "medicalExam",
                        principalColumn: "examId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberTransfer",
                schema: "patient",
                columns: table => new
                {
                    transferId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    fromCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    toCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    fromMemberCenterIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    toMemberCenterIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    transferReason = table.Column<string>(type: "text", nullable: false),
                    executedByUserId = table.Column<string>(type: "text", nullable: false),
                    executedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberTransfer", x => x.transferId);
                    table.CheckConstraint("ckMemberTransfer1", "\"fromCenterId\" <> \"toCenterId\"");
                    table.CheckConstraint("ckMemberTransfer2", "\"fromMemberCenterIdentityId\" <> \"toMemberCenterIdentityId\"");
                    table.ForeignKey(
                        name: "FK_memberTransfer_Users_executedByUserId",
                        column: x => x.executedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberTransfer_medicalCenter_fromCenterId",
                        column: x => x.fromCenterId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberTransfer_medicalCenter_toCenterId",
                        column: x => x.toCenterId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberTransfer_memberCenterIdentity_fromMemberCenterIdentit~",
                        columns: x => new { x.fromMemberCenterIdentityId, x.memberId, x.fromCenterId },
                        principalSchema: "patient",
                        principalTable: "memberCenterIdentity",
                        principalColumns: new[] { "memberCenterIdentityId", "memberId", "medicalCenterId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberTransfer_memberCenterIdentity_toMemberCenterIdentityI~",
                        columns: x => new { x.toMemberCenterIdentityId, x.memberId, x.toCenterId },
                        principalSchema: "patient",
                        principalTable: "memberCenterIdentity",
                        principalColumns: new[] { "memberCenterIdentityId", "memberId", "medicalCenterId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberTransfer_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deferredAction",
                schema: "clinical",
                columns: table => new
                {
                    deferredActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    clinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    clinicAssessmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    finalDecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    deferReason = table.Column<string>(type: "text", nullable: false),
                    deferUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    actionTypeId = table.Column<short>(type: "smallint", nullable: false),
                    targetClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    actionDescription = table.Column<string>(type: "text", nullable: true),
                    followupNote = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "citext", nullable: false, defaultValue: "REQUESTED"),
                    requestedByUserId = table.Column<string>(type: "text", nullable: false),
                    requestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completedByUserId = table.Column<string>(type: "text", nullable: true),
                    completedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deferredAction", x => x.deferredActionId);
                    table.CheckConstraint("ckDeferredAction1", "(\"clinicAssessmentId\" IS NOT NULL) <> (\"finalDecisionId\" IS NOT NULL)");
                    table.CheckConstraint("ckDeferredAction2", "\"status\" IN ('REQUESTED','COMPLETED','CLOSED')");
                    table.CheckConstraint("ckDeferredAction3", "((\"clinicAssessmentId\" IS NOT NULL AND \"finalDecisionId\" IS NULL AND \"clinicId\" IS NOT NULL) OR (\"finalDecisionId\" IS NOT NULL AND \"clinicAssessmentId\" IS NULL AND \"clinicId\" IS NULL))");
                    table.CheckConstraint("ckDeferredAction4", "btrim(\"deferReason\") <> ''");
                    table.CheckConstraint("ckDeferredAction6", "\"finalDecisionId\" IS NULL OR \"deferUntil\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_deferredAction_Users_completedByUserId",
                        column: x => x.completedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deferredAction_Users_requestedByUserId",
                        column: x => x.requestedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deferredAction_clinicAssessment_clinicAssessmentId_memberId~",
                        columns: x => new { x.clinicAssessmentId, x.memberId, x.centerId, x.clinicId },
                        principalSchema: "clinical",
                        principalTable: "clinicAssessment",
                        principalColumns: new[] { "assessmentId", "memberId", "centerId", "clinicId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deferredAction_clinic_clinicId",
                        column: x => x.clinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deferredAction_clinic_targetClinicId",
                        column: x => x.targetClinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deferredAction_finalDecision_finalDecisionId_memberId_cente~",
                        columns: x => new { x.finalDecisionId, x.memberId, x.centerId },
                        principalSchema: "clinical",
                        principalTable: "finalDecision",
                        principalColumns: new[] { "finalDecisionId", "memberId", "centerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deferredAction_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deferredAction_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deferredAction_requiredActionType_actionTypeId",
                        column: x => x.actionTypeId,
                        principalSchema: "core",
                        principalTable: "requiredActionType",
                        principalColumn: "actionTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supervisorObjection",
                schema: "clinical",
                columns: table => new
                {
                    objectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    supervisorUserId = table.Column<string>(type: "text", nullable: false),
                    targetScope = table.Column<string>(type: "citext", nullable: false),
                    clinicAssessmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    objectionReason = table.Column<string>(type: "text", nullable: false),
                    finalDecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "citext", nullable: false, defaultValue: "OPEN"),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolutionNote = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supervisorObjection", x => x.objectionId);
                    table.CheckConstraint("ckSupervisorObjection1", "\"targetScope\" IN ('ONE_CLINIC','ALL_CLINICS')");
                    table.CheckConstraint("ckSupervisorObjection2", "(\"targetScope\" = 'ONE_CLINIC' AND \"clinicAssessmentId\" IS NOT NULL) OR (\"targetScope\" = 'ALL_CLINICS' AND \"clinicAssessmentId\" IS NULL)");
                    table.CheckConstraint("ckSupervisorObjection3", "\"status\" IN ('OPEN','RESOLVED','DISMISSED')");
                    table.CheckConstraint("ckSupervisorObjection4", "(\"status\" = 'OPEN' AND \"resolvedAt\" IS NULL AND \"resolutionNote\" IS NULL) OR (\"status\" IN ('RESOLVED','DISMISSED') AND \"resolvedAt\" IS NOT NULL AND \"resolutionNote\" IS NOT NULL AND btrim(\"resolutionNote\") <> '')");
                    table.CheckConstraint("ckSupervisorObjection5", "\"resolvedAt\" IS NULL OR \"resolvedAt\" >= \"createdAt\"");
                    table.ForeignKey(
                        name: "FK_supervisorObjection_Users_supervisorUserId",
                        column: x => x.supervisorUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervisorObjection_clinicAssessment_clinicAssessmentId_mem~",
                        columns: x => new { x.clinicAssessmentId, x.memberId, x.centerId },
                        principalSchema: "clinical",
                        principalTable: "clinicAssessment",
                        principalColumns: new[] { "assessmentId", "memberId", "centerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervisorObjection_finalDecision_finalDecisionId_memberId_~",
                        columns: x => new { x.finalDecisionId, x.memberId, x.centerId },
                        principalSchema: "clinical",
                        principalTable: "finalDecision",
                        principalColumns: new[] { "finalDecisionId", "memberId", "centerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervisorObjection_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervisorObjection_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eyeRefractionDetail",
                schema: "clinical",
                columns: table => new
                {
                    refractionDetailId = table.Column<Guid>(type: "uuid", nullable: false),
                    examId = table.Column<Guid>(type: "uuid", nullable: false),
                    eyeSide = table.Column<string>(type: "citext", nullable: false),
                    refractionType = table.Column<string>(type: "citext", nullable: false),
                    inputFormat = table.Column<string>(type: "citext", nullable: false),
                    numerator = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    denominator = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    decimalInput = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    calculatedValue = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true, computedColumnSql: "CASE WHEN \"inputFormat\" = 'FRACTION' THEN (\"numerator\" / NULLIF(\"denominator\", 0)) ELSE \"decimalInput\" END", stored: true),
                    isWorst = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eyeRefractionDetail", x => x.refractionDetailId);
                    table.CheckConstraint("ckEyeRefractionDetail1", "\"eyeSide\" IN ('RIGHT','LEFT')");
                    table.CheckConstraint("ckEyeRefractionDetail2", "\"inputFormat\" IN ('FRACTION','DECIMAL')");
                    table.CheckConstraint("ckEyeRefractionDetail3", "(\"inputFormat\" = 'FRACTION' AND \"numerator\" IS NOT NULL AND \"denominator\" IS NOT NULL AND \"denominator\" > 0 AND \"decimalInput\" IS NULL) OR (\"inputFormat\" = 'DECIMAL' AND \"decimalInput\" IS NOT NULL AND \"numerator\" IS NULL AND \"denominator\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_eyeRefractionDetail_eyeExamDetail_examId",
                        column: x => x.examId,
                        principalSchema: "clinical",
                        principalTable: "eyeExamDetail",
                        principalColumn: "examId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bloodType_code",
                schema: "core",
                table: "bloodType",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bloodType_isActive_sortOrder",
                schema: "core",
                table: "bloodType",
                columns: new[] { "isActive", "sortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_bloodType_nameAr",
                schema: "core",
                table: "bloodType",
                column: "nameAr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bmiCategory_code",
                schema: "core",
                table: "bmiCategory",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bmiCategory_minBmi_maxBmi_isActive",
                schema: "core",
                table: "bmiCategory",
                columns: new[] { "minBmi", "maxBmi", "isActive" });

            migrationBuilder.CreateIndex(
                name: "IX_bmiCategory_nameAr",
                schema: "core",
                table: "bmiCategory",
                column: "nameAr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bmiCategory_sortOrder",
                schema: "core",
                table: "bmiCategory",
                column: "sortOrder",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_centerClinic_centerId_isActive",
                schema: "core",
                table: "centerClinic",
                columns: new[] { "centerId", "isActive" });

            migrationBuilder.CreateIndex(
                name: "IX_centerClinic_clinicId",
                schema: "core",
                table: "centerClinic",
                column: "clinicId");

            migrationBuilder.CreateIndex(
                name: "IX_clinic_code",
                schema: "core",
                table: "clinic",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clinic_sortOrder",
                schema: "core",
                table: "clinic",
                column: "sortOrder",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clinicAssessment_assessedByUserId",
                schema: "clinical",
                table: "clinicAssessment",
                column: "assessedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_clinicAssessment_centerId_resultStatusId",
                schema: "clinical",
                table: "clinicAssessment",
                columns: new[] { "centerId", "resultStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinicAssessment_clinicId",
                schema: "clinical",
                table: "clinicAssessment",
                column: "clinicId");

            migrationBuilder.CreateIndex(
                name: "IX_clinicAssessment_examId",
                schema: "clinical",
                table: "clinicAssessment",
                column: "examId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clinicAssessment_examId_memberId_centerId_clinicId",
                schema: "clinical",
                table: "clinicAssessment",
                columns: new[] { "examId", "memberId", "centerId", "clinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinicAssessment_memberId_clinicId",
                schema: "clinical",
                table: "clinicAssessment",
                columns: new[] { "memberId", "clinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinicAssessment_resultStatusId",
                schema: "clinical",
                table: "clinicAssessment",
                column: "resultStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_clinicAssessment_updatedByUserId",
                schema: "clinical",
                table: "clinicAssessment",
                column: "updatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_consultation_centerId",
                schema: "medical",
                table: "consultation",
                column: "centerId");

            migrationBuilder.CreateIndex(
                name: "IX_consultation_clinicId",
                schema: "medical",
                table: "consultation",
                column: "clinicId");

            migrationBuilder.CreateIndex(
                name: "IX_consultation_consultantUserId",
                schema: "medical",
                table: "consultation",
                column: "consultantUserId");

            migrationBuilder.CreateIndex(
                name: "IX_consultation_consultationTypeId",
                schema: "medical",
                table: "consultation",
                column: "consultationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_consultation_memberId_requestedAt",
                schema: "medical",
                table: "consultation",
                columns: new[] { "memberId", "requestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_consultation_requestedByUserId",
                schema: "medical",
                table: "consultation",
                column: "requestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_consultationType_code",
                schema: "core",
                table: "consultationType",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_consultationType_isActive_sortOrder",
                schema: "core",
                table: "consultationType",
                columns: new[] { "isActive", "sortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_consultationType_nameAr",
                schema: "core",
                table: "consultationType",
                column: "nameAr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crossCenterAccess_auditEventId_targetCenterId",
                schema: "audit",
                table: "crossCenterAccess",
                columns: new[] { "auditEventId", "targetCenterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crossCenterAccess_targetCenterId",
                schema: "audit",
                table: "crossCenterAccess",
                column: "targetCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_actionTypeId",
                schema: "clinical",
                table: "deferredAction",
                column: "actionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_centerId_status_deferUntil",
                schema: "clinical",
                table: "deferredAction",
                columns: new[] { "centerId", "status", "deferUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_clinicAssessmentId",
                schema: "clinical",
                table: "deferredAction",
                column: "clinicAssessmentId",
                unique: true,
                filter: "\"clinicAssessmentId\" IS NOT NULL AND \"status\" <> 'CLOSED'");

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_clinicAssessmentId_memberId_centerId_clinicId",
                schema: "clinical",
                table: "deferredAction",
                columns: new[] { "clinicAssessmentId", "memberId", "centerId", "clinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_clinicId",
                schema: "clinical",
                table: "deferredAction",
                column: "clinicId");

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_completedByUserId",
                schema: "clinical",
                table: "deferredAction",
                column: "completedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_finalDecisionId",
                schema: "clinical",
                table: "deferredAction",
                column: "finalDecisionId",
                unique: true,
                filter: "\"finalDecisionId\" IS NOT NULL AND \"status\" <> 'CLOSED'");

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_finalDecisionId_memberId_centerId",
                schema: "clinical",
                table: "deferredAction",
                columns: new[] { "finalDecisionId", "memberId", "centerId" });

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_memberId",
                schema: "clinical",
                table: "deferredAction",
                column: "memberId");

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_requestedByUserId",
                schema: "clinical",
                table: "deferredAction",
                column: "requestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_deferredAction_targetClinicId",
                schema: "clinical",
                table: "deferredAction",
                column: "targetClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_event_actorUserId_occurredAt",
                schema: "audit",
                table: "event",
                columns: new[] { "actorUserId", "occurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_event_centerId_occurredAt",
                schema: "audit",
                table: "event",
                columns: new[] { "centerId", "occurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_event_contextScope_occurredAt",
                schema: "audit",
                table: "event",
                columns: new[] { "contextScope", "occurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_event_entityType_entityId",
                schema: "audit",
                table: "event",
                columns: new[] { "entityType", "entityId" });

            migrationBuilder.CreateIndex(
                name: "IX_event_occurredAt",
                schema: "audit",
                table: "event",
                column: "occurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_eyeRefractionDetail_examId_eyeSide",
                schema: "clinical",
                table: "eyeRefractionDetail",
                columns: new[] { "examId", "eyeSide" });

            migrationBuilder.CreateIndex(
                name: "IX_eyeRefractionDetail_examId_eyeSide_refractionType",
                schema: "clinical",
                table: "eyeRefractionDetail",
                columns: new[] { "examId", "eyeSide", "refractionType" },
                unique: true,
                filter: "\"isWorst\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_fileAsset_centerId_createdAt",
                schema: "core",
                table: "fileAsset",
                columns: new[] { "centerId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_fileAsset_storageKey",
                schema: "core",
                table: "fileAsset",
                column: "storageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fileAsset_uploadedByUserId",
                schema: "core",
                table: "fileAsset",
                column: "uploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_finalDecision_centerId_resultStatusId_decidedAt",
                schema: "clinical",
                table: "finalDecision",
                columns: new[] { "centerId", "resultStatusId", "decidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_finalDecision_memberId",
                schema: "clinical",
                table: "finalDecision",
                column: "memberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finalDecision_resultStatusId",
                schema: "clinical",
                table: "finalDecision",
                column: "resultStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_finalDecision_supervisorUserId",
                schema: "clinical",
                table: "finalDecision",
                column: "supervisorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_finalDecision_updatedByUserId",
                schema: "clinical",
                table: "finalDecision",
                column: "updatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_finalDecisionHistory_centerId",
                schema: "clinical",
                table: "finalDecisionHistory",
                column: "centerId");

            migrationBuilder.CreateIndex(
                name: "IX_finalDecisionHistory_changedByUserId",
                schema: "clinical",
                table: "finalDecisionHistory",
                column: "changedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_finalDecisionHistory_finalDecisionId_memberId_centerId",
                schema: "clinical",
                table: "finalDecisionHistory",
                columns: new[] { "finalDecisionId", "memberId", "centerId" });

            migrationBuilder.CreateIndex(
                name: "IX_finalDecisionHistory_memberId_changedAt",
                schema: "clinical",
                table: "finalDecisionHistory",
                columns: new[] { "memberId", "changedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_finalDecisionHistory_newStatusId",
                schema: "clinical",
                table: "finalDecisionHistory",
                column: "newStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_finalDecisionHistory_oldStatusId",
                schema: "clinical",
                table: "finalDecisionHistory",
                column: "oldStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_investigation_centerId_status",
                schema: "medical",
                table: "investigation",
                columns: new[] { "centerId", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_investigation_clinicId",
                schema: "medical",
                table: "investigation",
                column: "clinicId");

            migrationBuilder.CreateIndex(
                name: "IX_investigation_investigationTypeId",
                schema: "medical",
                table: "investigation",
                column: "investigationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_investigation_memberId_requestedAt",
                schema: "medical",
                table: "investigation",
                columns: new[] { "memberId", "requestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_investigation_requestedByUserId",
                schema: "medical",
                table: "investigation",
                column: "requestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_investigationType_code",
                schema: "core",
                table: "investigationType",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_investigationType_isActive_sortOrder",
                schema: "core",
                table: "investigationType",
                columns: new[] { "isActive", "sortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_investigationType_nameAr",
                schema: "core",
                table: "investigationType",
                column: "nameAr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maritalStatus_code",
                schema: "core",
                table: "maritalStatus",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maritalStatus_isActive_sortOrder",
                schema: "core",
                table: "maritalStatus",
                columns: new[] { "isActive", "sortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_maritalStatus_nameAr",
                schema: "core",
                table: "maritalStatus",
                column: "nameAr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicalAttachment_centerId",
                schema: "medical",
                table: "medicalAttachment",
                column: "centerId");

            migrationBuilder.CreateIndex(
                name: "IX_medicalAttachment_consultationId_memberId_centerId",
                schema: "medical",
                table: "medicalAttachment",
                columns: new[] { "consultationId", "memberId", "centerId" });

            migrationBuilder.CreateIndex(
                name: "IX_medicalAttachment_fileAssetId",
                schema: "medical",
                table: "medicalAttachment",
                column: "fileAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicalAttachment_investigationId_memberId_centerId",
                schema: "medical",
                table: "medicalAttachment",
                columns: new[] { "investigationId", "memberId", "centerId" });

            migrationBuilder.CreateIndex(
                name: "IX_medicalAttachment_memberId_createdAt",
                schema: "medical",
                table: "medicalAttachment",
                columns: new[] { "memberId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_medicalAttachment_uploadedByUserId",
                schema: "medical",
                table: "medicalAttachment",
                column: "uploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_medicalCenter_code",
                schema: "core",
                table: "medicalCenter",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicalCenter_isActive",
                schema: "core",
                table: "medicalCenter",
                column: "isActive");

            migrationBuilder.CreateIndex(
                name: "IX_medicalExam_centerId_clinicId_examinedAt",
                schema: "clinical",
                table: "medicalExam",
                columns: new[] { "centerId", "clinicId", "examinedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_medicalExam_clinicId",
                schema: "clinical",
                table: "medicalExam",
                column: "clinicId");

            migrationBuilder.CreateIndex(
                name: "IX_medicalExam_examinerUserId",
                schema: "clinical",
                table: "medicalExam",
                column: "examinerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_medicalExam_memberId",
                schema: "clinical",
                table: "medicalExam",
                column: "memberId");

            migrationBuilder.CreateIndex(
                name: "IX_medicalExam_memberId_clinicId",
                schema: "clinical",
                table: "medicalExam",
                columns: new[] { "memberId", "clinicId" },
                unique: true,
                filter: "\"isCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_medicalExam_memberId_clinicId_examSequence",
                schema: "clinical",
                table: "medicalExam",
                columns: new[] { "memberId", "clinicId", "examSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicalExam_updatedByUserId",
                schema: "clinical",
                table: "medicalExam",
                column: "updatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_member_bloodTypeId",
                schema: "patient",
                table: "member",
                column: "bloodTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_member_createdByUserId",
                schema: "patient",
                table: "member",
                column: "createdByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_member_currentMedicalCenterId_createdAt",
                schema: "patient",
                table: "member",
                columns: new[] { "currentMedicalCenterId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_member_currentMedicalCenterId_exportStatus",
                schema: "patient",
                table: "member",
                columns: new[] { "currentMedicalCenterId", "exportStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_member_currentMedicalCenterId_reviewedRevision_dataRevision",
                schema: "patient",
                table: "member",
                columns: new[] { "currentMedicalCenterId", "reviewedRevision", "dataRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_member_fileNumber",
                schema: "patient",
                table: "member",
                column: "fileNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_fullName",
                schema: "patient",
                table: "member",
                column: "fullName");

            migrationBuilder.CreateIndex(
                name: "IX_member_maritalStatusId",
                schema: "patient",
                table: "member",
                column: "maritalStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_member_queueMedicalCenterId_queueDate_queueNumber",
                schema: "patient",
                table: "member",
                columns: new[] { "queueMedicalCenterId", "queueDate", "queueNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_recruitmentCenterId",
                schema: "patient",
                table: "member",
                column: "recruitmentCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_member_recruitmentNumber",
                schema: "patient",
                table: "member",
                column: "recruitmentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_reviewedByUserId",
                schema: "patient",
                table: "member",
                column: "reviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_member_updatedByUserId",
                schema: "patient",
                table: "member",
                column: "updatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_memberArchive_archiveNumber",
                schema: "archive",
                table: "memberArchive",
                column: "archiveNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberArchive_centerId_archivedAt",
                schema: "archive",
                table: "memberArchive",
                columns: new[] { "centerId", "archivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_memberArchive_digitalCopyFileId",
                schema: "archive",
                table: "memberArchive",
                column: "digitalCopyFileId");

            migrationBuilder.CreateIndex(
                name: "IX_memberArchive_finalDecisionId",
                schema: "archive",
                table: "memberArchive",
                column: "finalDecisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberArchive_finalDecisionId_memberId_centerId",
                schema: "archive",
                table: "memberArchive",
                columns: new[] { "finalDecisionId", "memberId", "centerId" });

            migrationBuilder.CreateIndex(
                name: "IX_memberArchive_memberId",
                schema: "archive",
                table: "memberArchive",
                column: "memberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberCenterIdentity_assignedByUserId",
                schema: "patient",
                table: "memberCenterIdentity",
                column: "assignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_memberCenterIdentity_closedByUserId",
                schema: "patient",
                table: "memberCenterIdentity",
                column: "closedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_memberCenterIdentity_medicalCenterId_centerMemberId",
                schema: "patient",
                table: "memberCenterIdentity",
                columns: new[] { "medicalCenterId", "centerMemberId" },
                unique: true,
                filter: "\"validTo\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_memberCenterIdentity_memberId",
                schema: "patient",
                table: "memberCenterIdentity",
                column: "memberId",
                unique: true,
                filter: "\"validTo\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_memberCenterIdentity_memberId_validFrom",
                schema: "patient",
                table: "memberCenterIdentity",
                columns: new[] { "memberId", "validFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_memberGeneralNote_authorClinicId",
                schema: "patient",
                table: "memberGeneralNote",
                column: "authorClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_memberGeneralNote_authorUserId",
                schema: "patient",
                table: "memberGeneralNote",
                column: "authorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_memberGeneralNote_centerId",
                schema: "patient",
                table: "memberGeneralNote",
                column: "centerId");

            migrationBuilder.CreateIndex(
                name: "IX_memberGeneralNote_memberId",
                schema: "patient",
                table: "memberGeneralNote",
                column: "memberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberGeneralNote_updatedByUserId",
                schema: "patient",
                table: "memberGeneralNote",
                column: "updatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_memberTransfer_executedByUserId",
                schema: "patient",
                table: "memberTransfer",
                column: "executedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_memberTransfer_fromCenterId",
                schema: "patient",
                table: "memberTransfer",
                column: "fromCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_memberTransfer_fromMemberCenterIdentityId_memberId_fromCent~",
                schema: "patient",
                table: "memberTransfer",
                columns: new[] { "fromMemberCenterIdentityId", "memberId", "fromCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_memberTransfer_fromMemberCenterIdentityId_toMemberCenterIde~",
                schema: "patient",
                table: "memberTransfer",
                columns: new[] { "fromMemberCenterIdentityId", "toMemberCenterIdentityId" });

            migrationBuilder.CreateIndex(
                name: "IX_memberTransfer_memberId_executedAt",
                schema: "patient",
                table: "memberTransfer",
                columns: new[] { "memberId", "executedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_memberTransfer_toCenterId",
                schema: "patient",
                table: "memberTransfer",
                column: "toCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_memberTransfer_toMemberCenterIdentityId_memberId_toCenterId",
                schema: "patient",
                table: "memberTransfer",
                columns: new[] { "toMemberCenterIdentityId", "memberId", "toCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_passwordPolicy_isActive",
                schema: "iam",
                table: "passwordPolicy",
                column: "isActive",
                unique: true,
                filter: "\"isActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_passwordPolicy_isActive_validFrom",
                schema: "iam",
                table: "passwordPolicy",
                columns: new[] { "isActive", "validFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_passwordPolicy_updatedByUserId",
                schema: "iam",
                table: "passwordPolicy",
                column: "updatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_passwordPolicy_versionNo",
                schema: "iam",
                table: "passwordPolicy",
                column: "versionNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Name",
                schema: "iam",
                table: "Permissions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentCenter_code",
                schema: "core",
                table: "recruitmentCenter",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentCenter_isActive",
                schema: "core",
                table: "recruitmentCenter",
                column: "isActive");

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentExportBatch_centerId_createdAt",
                schema: "export",
                table: "recruitmentExportBatch",
                columns: new[] { "centerId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentExportBatch_exportedByUserId",
                schema: "export",
                table: "recruitmentExportBatch",
                column: "exportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentExportBatch_generatedFileId",
                schema: "export",
                table: "recruitmentExportBatch",
                column: "generatedFileId");

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentExportItem_finalDecisionId_memberId",
                schema: "export",
                table: "recruitmentExportItem",
                columns: new[] { "finalDecisionId", "memberId" });

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentExportItem_memberId_exportedAt",
                schema: "export",
                table: "recruitmentExportItem",
                columns: new[] { "memberId", "exportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentExportItem_previousExportBatchId_memberId",
                schema: "export",
                table: "recruitmentExportItem",
                columns: new[] { "previousExportBatchId", "memberId" });

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentExportItem_recruitmentCenterId",
                schema: "export",
                table: "recruitmentExportItem",
                column: "recruitmentCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_recruitmentExportItem_resultStatusId",
                schema: "export",
                table: "recruitmentExportItem",
                column: "resultStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_refreshToken_parentRefreshTokenId",
                schema: "iam",
                table: "refreshToken",
                column: "parentRefreshTokenId",
                unique: true,
                filter: "\"parentRefreshTokenId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_refreshToken_replacedByRefreshTokenId",
                schema: "iam",
                table: "refreshToken",
                column: "replacedByRefreshTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_refreshToken_tokenFamilyId_issuedAt",
                schema: "iam",
                table: "refreshToken",
                columns: new[] { "tokenFamilyId", "issuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_refreshToken_tokenHash",
                schema: "iam",
                table: "refreshToken",
                column: "tokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refreshToken_userId_expiresAt",
                schema: "iam",
                table: "refreshToken",
                columns: new[] { "userId", "expiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_requiredActionType_code",
                schema: "core",
                table: "requiredActionType",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requiredActionType_nameAr",
                schema: "core",
                table: "requiredActionType",
                column: "nameAr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resultStatus_code",
                schema: "core",
                table: "resultStatus",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resultStatus_nameAr",
                schema: "core",
                table: "resultStatus",
                column: "nameAr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleFieldPermissions_FieldPermissionId",
                schema: "iam",
                table: "RoleFieldPermissions",
                column: "FieldPermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleFieldPermissions_RoleId",
                schema: "iam",
                table: "RoleFieldPermissions",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_GrantedByUserId",
                schema: "iam",
                table: "RolePermissions",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                schema: "iam",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RevokedByUserId",
                schema: "iam",
                table: "RolePermissions",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId",
                schema: "iam",
                table: "RolePermissions",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_supervisorObjection_centerId",
                schema: "clinical",
                table: "supervisorObjection",
                column: "centerId");

            migrationBuilder.CreateIndex(
                name: "IX_supervisorObjection_clinicAssessmentId_memberId_centerId",
                schema: "clinical",
                table: "supervisorObjection",
                columns: new[] { "clinicAssessmentId", "memberId", "centerId" });

            migrationBuilder.CreateIndex(
                name: "IX_supervisorObjection_finalDecisionId_memberId_centerId",
                schema: "clinical",
                table: "supervisorObjection",
                columns: new[] { "finalDecisionId", "memberId", "centerId" });

            migrationBuilder.CreateIndex(
                name: "IX_supervisorObjection_memberId_status",
                schema: "clinical",
                table: "supervisorObjection",
                columns: new[] { "memberId", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_supervisorObjection_supervisorUserId",
                schema: "clinical",
                table: "supervisorObjection",
                column: "supervisorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_userCenter_assignedByUserId",
                schema: "iam",
                table: "userCenter",
                column: "assignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_userCenter_centerId_isActive",
                schema: "iam",
                table: "userCenter",
                columns: new[] { "centerId", "isActive" });

            migrationBuilder.CreateIndex(
                name: "IX_userCenter_revokedByUserId",
                schema: "iam",
                table: "userCenter",
                column: "revokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_userClinicAssignment_assignedByUserId",
                schema: "iam",
                table: "userClinicAssignment",
                column: "assignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_userClinicAssignment_centerId_clinicId_isActive",
                schema: "iam",
                table: "userClinicAssignment",
                columns: new[] { "centerId", "clinicId", "isActive" });

            migrationBuilder.CreateIndex(
                name: "IX_userClinicAssignment_clinicId",
                schema: "iam",
                table: "userClinicAssignment",
                column: "clinicId");

            migrationBuilder.CreateIndex(
                name: "IX_userClinicAssignment_revokedByUserId",
                schema: "iam",
                table: "userClinicAssignment",
                column: "revokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_userClinicAssignment_userId_centerId_clinicId",
                schema: "iam",
                table: "userClinicAssignment",
                columns: new[] { "userId", "centerId", "clinicId" },
                unique: true,
                filter: "\"isActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_userContact_userId_contactType",
                schema: "iam",
                table: "userContact",
                columns: new[] { "userId", "contactType" },
                unique: true,
                filter: "\"isPrimary\" = true AND \"isActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_userContact_userId_contactType_contactValue",
                schema: "iam",
                table: "userContact",
                columns: new[] { "userId", "contactType", "contactValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_userContact_userId_isActive",
                schema: "iam",
                table: "userContact",
                columns: new[] { "userId", "isActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserFieldPermissions_FieldPermissionId",
                schema: "iam",
                table: "UserFieldPermissions",
                column: "FieldPermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFieldPermissions_UserId",
                schema: "iam",
                table: "UserFieldPermissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_userPasswordHistory_passwordPolicyId",
                schema: "iam",
                table: "userPasswordHistory",
                column: "passwordPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_userPasswordHistory_userId_setAt",
                schema: "iam",
                table: "userPasswordHistory",
                columns: new[] { "userId", "setAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_CenterId",
                schema: "iam",
                table: "UserPermissions",
                column: "CenterId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_GrantedByUserId",
                schema: "iam",
                table: "UserPermissions",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_PermissionId",
                schema: "iam",
                table: "UserPermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_RevokedByUserId",
                schema: "iam",
                table: "UserPermissions",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_UserId_CenterId_IsActive",
                schema: "iam",
                table: "UserPermissions",
                columns: new[] { "UserId", "CenterId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_UserId_PermissionId",
                schema: "iam",
                table: "UserPermissions",
                columns: new[] { "UserId", "PermissionId" },
                unique: true,
                filter: "\"CenterId\" IS NULL AND \"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_UserId_PermissionId_CenterId",
                schema: "iam",
                table: "UserPermissions",
                columns: new[] { "UserId", "PermissionId", "CenterId" },
                unique: true,
                filter: "\"CenterId\" IS NOT NULL AND \"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_userProfile_employeeNumber",
                schema: "iam",
                table: "userProfile",
                column: "employeeNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_userProfile_profileFileAssetId",
                schema: "iam",
                table: "userProfile",
                column: "profileFileAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_userProfile_updatedByUserId",
                schema: "iam",
                table: "userProfile",
                column: "updatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssociations_AssignedByUserId",
                schema: "iam",
                table: "UserRoleAssociations",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssociations_CenterId",
                schema: "iam",
                table: "UserRoleAssociations",
                column: "CenterId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssociations_RevokedByUserId",
                schema: "iam",
                table: "UserRoleAssociations",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssociations_RoleId",
                schema: "iam",
                table: "UserRoleAssociations",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssociations_UserId_CenterId_IsActive",
                schema: "iam",
                table: "UserRoleAssociations",
                columns: new[] { "UserId", "CenterId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssociations_UserId_RoleId",
                schema: "iam",
                table: "UserRoleAssociations",
                columns: new[] { "UserId", "RoleId" },
                unique: true,
                filter: "\"CenterId\" IS NULL AND \"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssociations_UserId_RoleId_CenterId",
                schema: "iam",
                table: "UserRoleAssociations",
                columns: new[] { "UserId", "RoleId", "CenterId" },
                unique: true,
                filter: "\"CenterId\" IS NOT NULL AND \"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_validationRule_code_isActive_validFrom_validTo",
                schema: "core",
                table: "validationRule",
                columns: new[] { "code", "isActive", "validFrom", "validTo" });

            migrationBuilder.CreateIndex(
                name: "IX_validationRule_code_versionNo",
                schema: "core",
                table: "validationRule",
                columns: new[] { "code", "versionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_validationRule_updatedByUserId",
                schema: "core",
                table: "validationRule",
                column: "updatedByUserId");

            foreach (var statement in InitialDatabaseObjects.UpStatements)
                migrationBuilder.Sql(statement);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var statement in InitialDatabaseObjects.DownStatements)
                migrationBuilder.Sql(statement);

            migrationBuilder.DropTable(
                name: "bmiCategory",
                schema: "core");

            migrationBuilder.DropTable(
                name: "crossCenterAccess",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "deferredAction",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "entExamDetail",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "eyeRefractionDetail",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "finalDecisionHistory",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "internalExamDetail",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "medicalAttachment",
                schema: "medical");

            migrationBuilder.DropTable(
                name: "memberArchive",
                schema: "archive");

            migrationBuilder.DropTable(
                name: "memberGeneralNote",
                schema: "patient");

            migrationBuilder.DropTable(
                name: "memberTransfer",
                schema: "patient");

            migrationBuilder.DropTable(
                name: "orthopedicExamDetail",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "recruitmentExportItem",
                schema: "export");

            migrationBuilder.DropTable(
                name: "refreshToken",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "RoleFieldPermissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "RolePermissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "supervisorObjection",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "surgeryExamDetail",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "userClinicAssignment",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "userContact",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "UserFieldPermissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "userPasswordHistory",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "UserPermissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "userProfile",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "UserRoleAssociations",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "validationRule",
                schema: "core");

            migrationBuilder.DropTable(
                name: "event",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "requiredActionType",
                schema: "core");

            migrationBuilder.DropTable(
                name: "eyeExamDetail",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "consultation",
                schema: "medical");

            migrationBuilder.DropTable(
                name: "investigation",
                schema: "medical");

            migrationBuilder.DropTable(
                name: "memberCenterIdentity",
                schema: "patient");

            migrationBuilder.DropTable(
                name: "recruitmentExportBatch",
                schema: "export");

            migrationBuilder.DropTable(
                name: "clinicAssessment",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "finalDecision",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "centerClinic",
                schema: "core");

            migrationBuilder.DropTable(
                name: "userCenter",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "FieldPermissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "passwordPolicy",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "Permissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "consultationType",
                schema: "core");

            migrationBuilder.DropTable(
                name: "investigationType",
                schema: "core");

            migrationBuilder.DropTable(
                name: "fileAsset",
                schema: "core");

            migrationBuilder.DropTable(
                name: "medicalExam",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "resultStatus",
                schema: "core");

            migrationBuilder.DropTable(
                name: "clinic",
                schema: "core");

            migrationBuilder.DropTable(
                name: "member",
                schema: "patient");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "bloodType",
                schema: "core");

            migrationBuilder.DropTable(
                name: "maritalStatus",
                schema: "core");

            migrationBuilder.DropTable(
                name: "medicalCenter",
                schema: "core");

            migrationBuilder.DropTable(
                name: "recruitmentCenter",
                schema: "core");
        }
    }
}
