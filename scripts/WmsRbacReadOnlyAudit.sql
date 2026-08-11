SET NOCOUNT ON;

WITH ExpectedRoles(RoleName) AS
(
    SELECT RoleName
    FROM (VALUES
        ('Admin'), ('Manager'), ('Staff'), ('InboundStaff'), ('OutboundStaff'),
        ('InventoryStaff'), ('TransportStaff'), ('ReportViewer'), ('Viewer')) value(RoleName)
)
SELECT 'RBAC_MISSING_ROLE' AS IssueCode, expected.RoleName
FROM ExpectedRoles expected
WHERE NOT EXISTS
(
    SELECT 1 FROM [dbo].[AppRoles] roleRow WHERE roleRow.RoleName = expected.RoleName
);

SELECT 'RBAC_ADMIN_MISSING_PERMISSION' AS IssueCode, permissionRow.Code
FROM [dbo].[Permissions] permissionRow
WHERE NOT EXISTS
(
    SELECT 1
    FROM [dbo].[AppRoles] roleRow
    INNER JOIN [dbo].[RolePermissions] rolePermission ON rolePermission.RoleId = roleRow.RoleId
    WHERE roleRow.RoleName = 'Admin'
      AND rolePermission.PermissionId = permissionRow.PermissionId
);

WITH ExpectedGrants(RoleName, PermissionCode) AS
(
    SELECT RoleName, PermissionCode
    FROM (VALUES
        ('Manager', 'voucher.create'),
        ('Manager', 'voucher.approve.inbound'),
        ('Manager', 'voucher.approve.outbound'),
        ('Manager', 'voucher.cancel'),
        ('Manager', 'voucher.post.outbound'),
        ('Manager', 'voucher.release.picking'),
        ('Manager', 'voucher.confirm.shipping'),
        ('Manager', 'qc.submit.inspection'),
        ('Manager', 'qc.resolve.hold'),
        ('Manager', 'stockcount.approve'),
        ('Manager', 'master.item.manage'),
        ('Manager', 'master.partner.manage'),
        ('Manager', 'master.category.manage'),
        ('Manager', 'master.uom.manage'),
        ('Manager', 'warehouse.config.manage'),
        ('Manager', 'report.view'),
        ('Manager', 'report.view.financial'),
        ('Manager', 'picktask.reassign'),
        ('Manager', 'tenant.scope.manage'),
        ('Manager', 'billing.3pl.manage'),
        ('Manager', 'mhe.manage'),
        ('Staff', 'voucher.create'),
        ('Staff', 'report.view'),
        ('InboundStaff', 'voucher.create'),
        ('InboundStaff', 'qc.submit.inspection'),
        ('InboundStaff', 'report.view'),
        ('OutboundStaff', 'voucher.create'),
        ('OutboundStaff', 'report.view'),
        ('InventoryStaff', 'voucher.create'),
        ('InventoryStaff', 'report.view'),
        ('TransportStaff', 'voucher.confirm.shipping'),
        ('TransportStaff', 'report.view'),
        ('ReportViewer', 'report.view'),
        ('Viewer', 'report.view')) value(RoleName, PermissionCode)
)
SELECT 'RBAC_EXPECTED_GRANT_MISSING' AS IssueCode, expected.RoleName, expected.PermissionCode
FROM ExpectedGrants expected
WHERE NOT EXISTS
(
    SELECT 1
    FROM [dbo].[AppRoles] roleRow
    INNER JOIN [dbo].[RolePermissions] rolePermission ON rolePermission.RoleId = roleRow.RoleId
    INNER JOIN [dbo].[Permissions] permissionRow ON permissionRow.PermissionId = rolePermission.PermissionId
    WHERE roleRow.RoleName = expected.RoleName
      AND permissionRow.Code = expected.PermissionCode
);

WITH ExpectedGrants(RoleName, PermissionCode) AS
(
    SELECT RoleName, PermissionCode
    FROM (VALUES
        ('Manager', 'voucher.create'), ('Manager', 'voucher.approve.inbound'),
        ('Manager', 'voucher.approve.outbound'), ('Manager', 'voucher.cancel'),
        ('Manager', 'voucher.post.outbound'), ('Manager', 'voucher.release.picking'),
        ('Manager', 'voucher.confirm.shipping'), ('Manager', 'qc.submit.inspection'),
        ('Manager', 'qc.resolve.hold'), ('Manager', 'stockcount.approve'),
        ('Manager', 'master.item.manage'), ('Manager', 'master.partner.manage'),
        ('Manager', 'master.category.manage'), ('Manager', 'master.uom.manage'),
        ('Manager', 'warehouse.config.manage'), ('Manager', 'report.view'),
        ('Manager', 'report.view.financial'), ('Manager', 'picktask.reassign'),
        ('Manager', 'tenant.scope.manage'), ('Manager', 'billing.3pl.manage'),
        ('Manager', 'mhe.manage'), ('Staff', 'voucher.create'), ('Staff', 'report.view'),
        ('InboundStaff', 'voucher.create'), ('InboundStaff', 'qc.submit.inspection'),
        ('InboundStaff', 'report.view'), ('OutboundStaff', 'voucher.create'),
        ('OutboundStaff', 'report.view'), ('InventoryStaff', 'voucher.create'),
        ('InventoryStaff', 'report.view'), ('TransportStaff', 'voucher.confirm.shipping'),
        ('TransportStaff', 'report.view'), ('ReportViewer', 'report.view'), ('Viewer', 'report.view')) value(RoleName, PermissionCode)
)
SELECT 'RBAC_UNEXPECTED_NON_ADMIN_GRANT' AS IssueCode, roleRow.RoleName, permissionRow.Code
FROM [dbo].[AppRoles] roleRow
INNER JOIN [dbo].[RolePermissions] rolePermission ON rolePermission.RoleId = roleRow.RoleId
INNER JOIN [dbo].[Permissions] permissionRow ON permissionRow.PermissionId = rolePermission.PermissionId
WHERE roleRow.RoleName <> 'Admin'
  AND roleRow.RoleName IN ('Manager', 'Staff', 'InboundStaff', 'OutboundStaff', 'InventoryStaff', 'TransportStaff', 'ReportViewer', 'Viewer')
  AND NOT EXISTS
  (
      SELECT 1 FROM ExpectedGrants expected
      WHERE expected.RoleName = roleRow.RoleName
        AND expected.PermissionCode = permissionRow.Code
  );

SELECT 'RBAC_DUPLICATE_ROLE_OR_PERMISSION' AS IssueCode, duplicateRow.EntityType, duplicateRow.BusinessKey, duplicateRow.DuplicateCount
FROM
(
    SELECT 'Role' AS EntityType, RoleName AS BusinessKey, COUNT_BIG(*) AS DuplicateCount
    FROM [dbo].[AppRoles]
    GROUP BY RoleName
    HAVING COUNT_BIG(*) > 1
    UNION ALL
    SELECT 'Permission', Code, COUNT_BIG(*)
    FROM [dbo].[Permissions]
    GROUP BY Code
    HAVING COUNT_BIG(*) > 1
) duplicateRow;

SELECT 'RBAC_ROLE_SUMMARY' AS IssueCode, roleRow.RoleName, roleRow.RoleId,
       COUNT(DISTINCT rolePermission.PermissionId) AS PermissionCount,
       COUNT(DISTINCT CASE WHEN userRow.IsActive = 1 THEN userRow.UserId END) AS ActiveUserCount
FROM [dbo].[AppRoles] roleRow
LEFT JOIN [dbo].[RolePermissions] rolePermission ON rolePermission.RoleId = roleRow.RoleId
LEFT JOIN [dbo].[AppUsers] userRow ON userRow.RoleId = roleRow.RoleId
WHERE roleRow.RoleName IN ('Admin', 'Manager', 'Staff', 'InboundStaff', 'OutboundStaff', 'InventoryStaff', 'TransportStaff', 'ReportViewer', 'Viewer')
GROUP BY roleRow.RoleName, roleRow.RoleId
ORDER BY roleRow.RoleName;
