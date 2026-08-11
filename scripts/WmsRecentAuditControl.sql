SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT TOP (10)
    'RECENT_AUDIT_CONTROL' AS IssueCode,
    [AuditLogId],
    [TableName],
    [ActionType],
    [AppModule],
    [ChangedAt]
FROM [dbo].[AuditLogs]
ORDER BY [AuditLogId] DESC;
