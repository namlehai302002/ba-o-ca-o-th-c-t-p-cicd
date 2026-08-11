SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    'STARTUP_READ_ONLY_CONTROL' AS IssueCode,
    (SELECT COUNT_BIG(*) FROM [dbo].[AppRoles]) AS RoleCount,
    (SELECT ISNULL(CHECKSUM_AGG(BINARY_CHECKSUM([RoleId], [RoleName], [Description])), 0)
     FROM [dbo].[AppRoles]) AS RoleChecksum,
    (SELECT COUNT_BIG(*) FROM [dbo].[Permissions]) AS PermissionCount,
    (SELECT ISNULL(CHECKSUM_AGG(BINARY_CHECKSUM([PermissionId], [Code], [Description], [UpdatedAt])), 0)
     FROM [dbo].[Permissions]) AS PermissionChecksum,
    (SELECT COUNT_BIG(*) FROM [dbo].[RolePermissions]) AS RolePermissionCount,
    (SELECT ISNULL(CHECKSUM_AGG(BINARY_CHECKSUM([RoleId], [PermissionId], [CreatedAt])), 0)
     FROM [dbo].[RolePermissions]) AS RolePermissionChecksum,
    (SELECT COUNT_BIG(*) FROM [dbo].[AuditLogs]) AS AuditLogCount,
    (SELECT ISNULL(MAX([AuditLogId]), 0) FROM [dbo].[AuditLogs]) AS MaxAuditLogId;
