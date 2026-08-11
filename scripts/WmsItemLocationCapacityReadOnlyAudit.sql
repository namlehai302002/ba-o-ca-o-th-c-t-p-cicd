SET NOCOUNT ON;

SELECT 'ITEMLOCATIONS_TABLE_MISSING' AS IssueCode, N'ItemLocations' AS TableName
WHERE NOT EXISTS
(
    SELECT 1
    FROM sys.tables tableRow
    WHERE tableRow.name = N'ItemLocations'
);

SELECT 'ITEMLOCATIONS_MAX_CAPACITY_MISSING' AS IssueCode, schemaRow.name AS SchemaName, tableRow.name AS TableName
FROM sys.tables tableRow
INNER JOIN sys.schemas schemaRow ON schemaRow.schema_id = tableRow.schema_id
WHERE tableRow.name = N'ItemLocations'
  AND NOT EXISTS
  (
      SELECT 1
      FROM sys.columns columnRow
      WHERE columnRow.object_id = tableRow.object_id
        AND columnRow.name = N'MaxCapacity'
  );

SELECT
    'ITEMLOCATIONS_MAX_CAPACITY_SHAPE_MISMATCH' AS IssueCode,
    schemaRow.name AS SchemaName,
    tableRow.name AS TableName,
    typeRow.name AS TypeName,
    columnRow.precision AS NumericPrecision,
    columnRow.scale AS NumericScale,
    columnRow.is_nullable AS IsNullable
FROM sys.tables tableRow
INNER JOIN sys.schemas schemaRow ON schemaRow.schema_id = tableRow.schema_id
INNER JOIN sys.columns columnRow ON columnRow.object_id = tableRow.object_id AND columnRow.name = N'MaxCapacity'
INNER JOIN sys.types typeRow ON typeRow.user_type_id = columnRow.user_type_id
WHERE tableRow.name = N'ItemLocations'
  AND
  (
      typeRow.name <> N'decimal'
      OR columnRow.precision <> 18
      OR columnRow.scale <> 4
      OR columnRow.is_nullable <> 1
  );

SELECT 'ITEMLOCATIONS_TOTAL_CAPACITY_MISSING' AS IssueCode, schemaRow.name AS SchemaName, tableRow.name AS TableName
FROM sys.tables tableRow
INNER JOIN sys.schemas schemaRow ON schemaRow.schema_id = tableRow.schema_id
WHERE tableRow.name = N'ItemLocations'
  AND NOT EXISTS
  (
      SELECT 1
      FROM sys.columns columnRow
      WHERE columnRow.object_id = tableRow.object_id
        AND columnRow.name = N'TotalCapacity'
  );
