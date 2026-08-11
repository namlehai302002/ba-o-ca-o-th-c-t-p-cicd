/*
Read-only database metadata audit for WMS.
The script returns schema and aggregate metadata only. It does not read business row values.
*/

SET NOCOUNT ON;

SELECT
    'DB_ENGINE' AS IssueCode,
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS ProductVersion,
    CONVERT(nvarchar(128), SERVERPROPERTY('Edition')) AS Edition,
    CONVERT(int, SERVERPROPERTY('EngineEdition')) AS EngineEdition;

SELECT
    'DATABASE_DEFAULTS' AS IssueCode,
    d.compatibility_level AS CompatibilityLevel,
    d.collation_name AS CollationName,
    d.is_read_committed_snapshot_on AS ReadCommittedSnapshot,
    d.snapshot_isolation_state_desc AS SnapshotIsolationState
FROM sys.databases d
WHERE d.database_id = DB_ID();

SELECT
    'CURRENT_PRINCIPAL_DEFAULT_SCHEMA' AS IssueCode,
    COALESCE(dp.default_schema_name, 'dbo') AS DefaultSchema
FROM sys.database_principals dp
WHERE dp.principal_id = DATABASE_PRINCIPAL_ID();

SELECT
    'SCHEMA_TABLE_COUNT' AS IssueCode,
    SCHEMA_NAME(t.schema_id) AS SchemaName,
    COUNT(*) AS TableCount
FROM sys.tables t
WHERE t.is_ms_shipped = 0
GROUP BY SCHEMA_NAME(t.schema_id)
ORDER BY SchemaName;

SELECT
    'SCHEMA_OBJECT_INVENTORY' AS IssueCode,
    SCHEMA_NAME(t.schema_id) AS SchemaName,
    t.name AS TableName,
    SUM(CASE WHEN p.index_id IN (0, 1) THEN p.rows ELSE 0 END) AS ApproximateRows
FROM sys.tables t
LEFT JOIN sys.partitions p ON p.object_id = t.object_id
WHERE t.is_ms_shipped = 0
GROUP BY t.schema_id, t.name
ORDER BY SchemaName, TableName;

SELECT
    'DATABASE_CONSTRAINT_COUNTS' AS IssueCode,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_ms_shipped = 0) AS ForeignKeys,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1) AS DisabledForeignKeys,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_not_trusted = 1) AS UntrustedForeignKeys,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE is_ms_shipped = 0) AS CheckConstraints,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled = 1) AS DisabledCheckConstraints,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables WHERE is_ms_shipped = 0) AND index_id > 0) AS Indexes,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables WHERE is_ms_shipped = 0) AND is_unique = 1) AS UniqueIndexes;

IF OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NOT NULL
BEGIN
    SELECT
        'MIGRATION_HISTORY' AS IssueCode,
        'dbo' AS SchemaName,
        COUNT(*) AS MigrationCount,
        MAX(MigrationId) AS LatestMigration
    FROM dbo.__EFMigrationsHistory;
END
ELSE IF OBJECT_ID('wms_user.__EFMigrationsHistory', 'U') IS NOT NULL
BEGIN
    SELECT
        'MIGRATION_HISTORY' AS IssueCode,
        'wms_user' AS SchemaName,
        COUNT(*) AS MigrationCount,
        MAX(MigrationId) AS LatestMigration
    FROM wms_user.__EFMigrationsHistory;
END
ELSE
BEGIN
    SELECT
        'MIGRATION_HISTORY' AS IssueCode,
        CAST(NULL AS nvarchar(128)) AS SchemaName,
        CAST(0 AS int) AS MigrationCount,
        CAST(NULL AS nvarchar(150)) AS LatestMigration;
END;

IF OBJECT_ID('dbo.AppUsers', 'U') IS NOT NULL AND OBJECT_ID('dbo.AppRoles', 'U') IS NOT NULL
BEGIN
    SELECT
        'ROLE_USER_COUNTS' AS IssueCode,
        r.RoleName,
        COUNT(u.UserId) AS UserCount,
        SUM(CASE WHEN u.IsActive = 1 THEN 1 ELSE 0 END) AS ActiveUserCount
    FROM dbo.AppRoles r
    LEFT JOIN dbo.AppUsers u ON u.RoleId = r.RoleId
    GROUP BY r.RoleName
    ORDER BY r.RoleName;
END
ELSE
BEGIN
    SELECT
        'ROLE_USER_COUNTS' AS IssueCode,
        CAST('UNAVAILABLE' AS nvarchar(100)) AS RoleName,
        CAST(0 AS int) AS UserCount,
        CAST(0 AS int) AS ActiveUserCount;
END;

SELECT
    'DATABASE_PERMISSION' AS IssueCode,
    permission_name AS PermissionName
FROM fn_my_permissions(NULL, 'DATABASE')
ORDER BY permission_name;
