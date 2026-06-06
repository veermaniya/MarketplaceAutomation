USE MarketplaceAutomation;
GO

/* ============================================================================
   sp_Product_ValidateDuplicates
   Section 5 rules - checked BEFORE insert/update from the app layer.
   Returns rows for any duplicate found. Empty result = OK to proceed.
   ============================================================================ */
IF OBJECT_ID('dbo.sp_Product_ValidateDuplicates','P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Product_ValidateDuplicates;
GO

CREATE PROCEDURE dbo.sp_Product_ValidateDuplicates
    @ProductId    INT = NULL,        -- pass for UPDATE; NULL for INSERT
    @SKU          NVARCHAR(100),
    @Barcode      NVARCHAR(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        'SKU' AS Field,
        p.ProductId AS ConflictingProductId,
        p.Title     AS ConflictingTitle,
        p.OwnerUserId
    FROM dbo.Products p
    WHERE p.SKU = @SKU
      AND (@ProductId IS NULL OR p.ProductId <> @ProductId)

    UNION ALL

    SELECT
        'Barcode',
        p.ProductId,
        p.Title,
        p.OwnerUserId
    FROM dbo.Products p
    WHERE @Barcode IS NOT NULL
      AND p.Barcode = @Barcode
      AND (@ProductId IS NULL OR p.ProductId <> @ProductId);
END
GO

/* ============================================================================
   sp_Mapping_ValidateExternal
   Prevents two users mapping the same ASIN/FSN on the same marketplace.
   ============================================================================ */
IF OBJECT_ID('dbo.sp_Mapping_ValidateExternal','P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Mapping_ValidateExternal;
GO

CREATE PROCEDURE dbo.sp_Mapping_ValidateExternal
    @MappingId         INT = NULL,
    @Marketplace       NVARCHAR(20),
    @ExternalListingId NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    IF @ExternalListingId IS NULL OR LEN(@ExternalListingId) = 0
    BEGIN
        SELECT TOP 0 1 AS Conflict;
        RETURN;
    END

    SELECT
        m.MappingId AS ConflictingMappingId,
        m.ProductId AS ConflictingProductId,
        p.Title     AS ConflictingTitle,
        p.OwnerUserId
    FROM dbo.MarketplaceMappings m
    INNER JOIN dbo.Products p ON p.ProductId = m.ProductId
    WHERE m.Marketplace = @Marketplace
      AND m.ExternalListingId = @ExternalListingId
      AND (@MappingId IS NULL OR m.MappingId <> @MappingId);
END
GO

/* ============================================================================
   sp_RetryQueue_DequeueBatch
   Atomically claims pending retries due for processing.
   Uses UPDLOCK + READPAST to allow safe concurrent workers.
   ============================================================================ */
IF OBJECT_ID('dbo.sp_RetryQueue_DequeueBatch','P') IS NOT NULL
    DROP PROCEDURE dbo.sp_RetryQueue_DequeueBatch;
GO

CREATE PROCEDURE dbo.sp_RetryQueue_DequeueBatch
    @BatchSize INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH cte AS (
        SELECT TOP (@BatchSize) *
        FROM dbo.RetryQueue WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE Status = 'Pending'
          AND NextAttemptOn <= SYSUTCDATETIME()
        ORDER BY NextAttemptOn
    )
    UPDATE cte
    SET Status      = 'InProgress',
        UpdatedOn   = SYSUTCDATETIME(),
        AttemptCount = AttemptCount + 1
    OUTPUT
        inserted.QueueId,
        inserted.JobType,
        inserted.Payload,
        inserted.AttemptCount,
        inserted.MaxAttempts;
END
GO

PRINT 'Stored procedures created.';
GO
