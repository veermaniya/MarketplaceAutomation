/* ============================================================================
   Multi-Marketplace Automation - SQL Server Schema
   Tables match Section 4 of the roadmap document.
   Duplicate-detection rules from Section 5 are enforced via filtered unique
   indexes (so NULLs don't collide).
   ============================================================================ */

IF DB_ID('MarketplaceAutomation') IS NULL
    CREATE DATABASE MarketplaceAutomation;
GO

USE MarketplaceAutomation;
GO

/* ---------- Drop in reverse-dependency order (safe re-run) ---------- */
IF OBJECT_ID('dbo.RetryQueue','U') IS NOT NULL DROP TABLE dbo.RetryQueue;
IF OBJECT_ID('dbo.AutomationLogs','U') IS NOT NULL DROP TABLE dbo.AutomationLogs;
IF OBJECT_ID('dbo.Orders','U') IS NOT NULL DROP TABLE dbo.Orders;
IF OBJECT_ID('dbo.Inventory','U') IS NOT NULL DROP TABLE dbo.Inventory;
IF OBJECT_ID('dbo.MarketplaceMappings','U') IS NOT NULL DROP TABLE dbo.MarketplaceMappings;
IF OBJECT_ID('dbo.MarketplaceAccounts','U') IS NOT NULL DROP TABLE dbo.MarketplaceAccounts;
IF OBJECT_ID('dbo.ProductImages','U') IS NOT NULL DROP TABLE dbo.ProductImages;
IF OBJECT_ID('dbo.Products','U') IS NOT NULL DROP TABLE dbo.Products;
IF OBJECT_ID('dbo.UserRoles','U') IS NOT NULL DROP TABLE dbo.UserRoles;
IF OBJECT_ID('dbo.Roles','U') IS NOT NULL DROP TABLE dbo.Roles;
IF OBJECT_ID('dbo.Users','U') IS NOT NULL DROP TABLE dbo.Users;
GO

/* ============================================================================
   USERS & ROLES
   ============================================================================ */
CREATE TABLE dbo.Users (
    UserId           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserName         NVARCHAR(100)     NOT NULL,
    Email            NVARCHAR(256)     NOT NULL,
    PasswordHash     NVARCHAR(512)     NOT NULL,
    PasswordSalt     NVARCHAR(256)     NOT NULL,
    IsActive         BIT               NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT(1),
    CreatedOn        DATETIME2(0)      NOT NULL CONSTRAINT DF_Users_CreatedOn DEFAULT(SYSUTCDATETIME()),
    LastLoginOn      DATETIME2(0)      NULL,
    CONSTRAINT UQ_Users_Email UNIQUE (Email)
);

CREATE TABLE dbo.Roles (
    RoleId           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RoleName         NVARCHAR(50)      NOT NULL,
    Description      NVARCHAR(200)     NULL,
    CONSTRAINT UQ_Roles_RoleName UNIQUE (RoleName)
);

CREATE TABLE dbo.UserRoles (
    UserId   INT NOT NULL,
    RoleId   INT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles(RoleId) ON DELETE CASCADE
);

/* ============================================================================
   PRODUCTS (master) + IMAGES
   ============================================================================ */
CREATE TABLE dbo.Products (
    ProductId        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId      INT               NOT NULL,
    SKU              NVARCHAR(100)     NOT NULL,
    Barcode          NVARCHAR(64)      NULL,
    Title            NVARCHAR(500)     NOT NULL,
    Description      NVARCHAR(MAX)     NULL,
    Brand            NVARCHAR(150)     NULL,
    Category         NVARCHAR(200)     NULL,
    MRP              DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Products_MRP DEFAULT(0),
    SellingPrice     DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Products_SP  DEFAULT(0),
    WeightGrams      INT               NULL,
    HSNCode          NVARCHAR(20)      NULL,
    GSTPercent       DECIMAL(5,2)      NULL,
    IsActive         BIT               NOT NULL CONSTRAINT DF_Products_IsActive DEFAULT(1),
    CreatedOn        DATETIME2(0)      NOT NULL CONSTRAINT DF_Products_Created DEFAULT(SYSUTCDATETIME()),
    UpdatedOn        DATETIME2(0)      NULL,
    CONSTRAINT FK_Products_Users FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(UserId)
);

/* Section 5: duplicate SKU prevention - global across all users to avoid
   the same product being mapped by multiple users (per roadmap requirement). */
CREATE UNIQUE INDEX UX_Products_SKU         ON dbo.Products(SKU);
CREATE UNIQUE INDEX UX_Products_Barcode     ON dbo.Products(Barcode) WHERE Barcode IS NOT NULL;
CREATE INDEX IX_Products_Owner              ON dbo.Products(OwnerUserId);
CREATE INDEX IX_Products_Category           ON dbo.Products(Category);

CREATE TABLE dbo.ProductImages (
    ImageId          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProductId        INT               NOT NULL,
    ImageUrl         NVARCHAR(1000)    NOT NULL,
    LocalPath        NVARCHAR(1000)    NULL,
    SortOrder        INT               NOT NULL CONSTRAINT DF_ProductImages_Sort DEFAULT(0),
    IsPrimary        BIT               NOT NULL CONSTRAINT DF_ProductImages_IsPrimary DEFAULT(0),
    CreatedOn        DATETIME2(0)      NOT NULL CONSTRAINT DF_ProductImages_Created DEFAULT(SYSUTCDATETIME()),
    CONSTRAINT FK_ProductImages_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(ProductId) ON DELETE CASCADE
);
CREATE INDEX IX_ProductImages_Product ON dbo.ProductImages(ProductId);

/* ============================================================================
   MARKETPLACE ACCOUNTS
   Credentials are stored encrypted (column-level) - the app encrypts before
   insert using DataProtection API. Never store plaintext.
   ============================================================================ */
CREATE TABLE dbo.MarketplaceAccounts (
    AccountId        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId           INT               NOT NULL,
    Marketplace      NVARCHAR(20)      NOT NULL,  -- 'Flipkart' | 'Amazon' | 'Meesho'
    DisplayName      NVARCHAR(200)     NOT NULL,
    EncryptedUserName NVARCHAR(MAX)    NOT NULL,
    EncryptedPassword NVARCHAR(MAX)    NOT NULL,
    EncryptedApiKey   NVARCHAR(MAX)    NULL,   -- Amazon SP-API LWA refresh token, etc.
    EncryptedApiSecret NVARCHAR(MAX)   NULL,
    SellerId         NVARCHAR(100)     NULL,   -- public seller id (Amazon)
    IsActive         BIT               NOT NULL CONSTRAINT DF_MPAcc_IsActive DEFAULT(1),
    CreatedOn        DATETIME2(0)      NOT NULL CONSTRAINT DF_MPAcc_Created DEFAULT(SYSUTCDATETIME()),
    LastUsedOn       DATETIME2(0)      NULL,
    CONSTRAINT FK_MPAccounts_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId),
    CONSTRAINT CK_MPAccounts_Marketplace CHECK (Marketplace IN ('Flipkart','Amazon','Meesho'))
);
CREATE INDEX IX_MPAccounts_User_Market ON dbo.MarketplaceAccounts(UserId, Marketplace);

/* ============================================================================
   MARKETPLACE MAPPINGS
   Links our Product to a marketplace listing. Section 5: prevent multiple
   users from mapping the same external listing (ASIN/FSN).
   ============================================================================ */
CREATE TABLE dbo.MarketplaceMappings (
    MappingId        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProductId        INT               NOT NULL,
    AccountId        INT               NOT NULL,
    Marketplace      NVARCHAR(20)      NOT NULL,
    ExternalListingId NVARCHAR(100)    NULL,   -- ASIN (Amazon), FSN (Flipkart), SKU (Meesho)
    ExternalSKU      NVARCHAR(100)     NULL,
    CategoryPath     NVARCHAR(500)     NULL,
    Status           NVARCHAR(30)      NOT NULL CONSTRAINT DF_MPMap_Status DEFAULT('Pending'),
        -- Pending | Listed | Failed | Disabled
    LastSyncedOn     DATETIME2(0)      NULL,
    LastError        NVARCHAR(MAX)     NULL,
    CreatedOn        DATETIME2(0)      NOT NULL CONSTRAINT DF_MPMap_Created DEFAULT(SYSUTCDATETIME()),
    CONSTRAINT FK_MPMap_Products  FOREIGN KEY (ProductId) REFERENCES dbo.Products(ProductId) ON DELETE CASCADE,
    CONSTRAINT FK_MPMap_Accounts  FOREIGN KEY (AccountId) REFERENCES dbo.MarketplaceAccounts(AccountId),
    CONSTRAINT CK_MPMap_Marketplace CHECK (Marketplace IN ('Flipkart','Amazon','Meesho'))
);

/* One product can be mapped only once per marketplace per account. */
CREATE UNIQUE INDEX UX_MPMap_Product_Account_Market
    ON dbo.MarketplaceMappings(ProductId, AccountId, Marketplace);

/* Section 5: an external listing (ASIN/FSN) can be claimed only once globally. */
CREATE UNIQUE INDEX UX_MPMap_External_Marketplace
    ON dbo.MarketplaceMappings(Marketplace, ExternalListingId)
    WHERE ExternalListingId IS NOT NULL;

CREATE INDEX IX_MPMap_Status ON dbo.MarketplaceMappings(Status);

/* ============================================================================
   INVENTORY (per marketplace mapping)
   ============================================================================ */
CREATE TABLE dbo.Inventory (
    InventoryId      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    MappingId        INT               NOT NULL,
    AvailableQty     INT               NOT NULL CONSTRAINT DF_Inv_Available DEFAULT(0),
    ReservedQty      INT               NOT NULL CONSTRAINT DF_Inv_Reserved  DEFAULT(0),
    Price            DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Inv_Price DEFAULT(0),
    LastPushedOn     DATETIME2(0)      NULL,
    LastPullOn       DATETIME2(0)      NULL,
    UpdatedOn        DATETIME2(0)      NOT NULL CONSTRAINT DF_Inv_Updated DEFAULT(SYSUTCDATETIME()),
    CONSTRAINT FK_Inventory_Mapping FOREIGN KEY (MappingId) REFERENCES dbo.MarketplaceMappings(MappingId) ON DELETE CASCADE
);
CREATE UNIQUE INDEX UX_Inventory_Mapping ON dbo.Inventory(MappingId);

/* ============================================================================
   ORDERS
   ============================================================================ */
CREATE TABLE dbo.Orders (
    OrderId          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    AccountId        INT               NOT NULL,
    Marketplace      NVARCHAR(20)      NOT NULL,
    MarketplaceOrderId NVARCHAR(100)   NOT NULL,
    ProductId        INT               NULL,    -- nullable: mapping might not exist yet
    MappingId        INT               NULL,
    OrderStatus      NVARCHAR(50)      NOT NULL,
    OrderQty         INT               NOT NULL,
    OrderAmount      DECIMAL(18,2)     NOT NULL,
    BuyerName        NVARCHAR(200)     NULL,
    BuyerCity        NVARCHAR(100)     NULL,
    OrderedOn        DATETIME2(0)      NOT NULL,
    FetchedOn        DATETIME2(0)      NOT NULL CONSTRAINT DF_Orders_Fetched DEFAULT(SYSUTCDATETIME()),
    RawPayload       NVARCHAR(MAX)     NULL,
    CONSTRAINT FK_Orders_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.MarketplaceAccounts(AccountId),
    CONSTRAINT FK_Orders_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(ProductId),
    CONSTRAINT FK_Orders_Mapping  FOREIGN KEY (MappingId) REFERENCES dbo.MarketplaceMappings(MappingId)
);
CREATE UNIQUE INDEX UX_Orders_Market_Ext ON dbo.Orders(Marketplace, MarketplaceOrderId);
CREATE INDEX IX_Orders_OrderedOn ON dbo.Orders(OrderedOn DESC);

/* ============================================================================
   AUTOMATION LOGS + RETRY QUEUE
   ============================================================================ */
CREATE TABLE dbo.AutomationLogs (
    LogId            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId           INT               NULL,
    AccountId        INT               NULL,
    MappingId        INT               NULL,
    Marketplace      NVARCHAR(20)      NULL,
    Action           NVARCHAR(100)     NOT NULL,    -- 'Login', 'CreateListing', 'PushInventory'...
    Status           NVARCHAR(20)      NOT NULL,    -- 'Started','Success','Failed'
    Message          NVARCHAR(MAX)     NULL,
    DurationMs       INT               NULL,
    OccurredOn       DATETIME2(0)      NOT NULL CONSTRAINT DF_AutoLogs_Occurred DEFAULT(SYSUTCDATETIME())
);
CREATE INDEX IX_AutoLogs_OccurredOn ON dbo.AutomationLogs(OccurredOn DESC);
CREATE INDEX IX_AutoLogs_Status     ON dbo.AutomationLogs(Status);

CREATE TABLE dbo.RetryQueue (
    QueueId          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    JobType          NVARCHAR(50)      NOT NULL,    -- 'CreateListing','PushInventory','FetchOrders'
    Payload          NVARCHAR(MAX)     NOT NULL,    -- JSON payload
    AttemptCount     INT               NOT NULL CONSTRAINT DF_Retry_Attempt DEFAULT(0),
    MaxAttempts      INT               NOT NULL CONSTRAINT DF_Retry_Max DEFAULT(5),
    NextAttemptOn    DATETIME2(0)      NOT NULL CONSTRAINT DF_Retry_Next DEFAULT(SYSUTCDATETIME()),
    Status           NVARCHAR(20)      NOT NULL CONSTRAINT DF_Retry_Status DEFAULT('Pending'),
        -- Pending | InProgress | Completed | Dead
    LastError        NVARCHAR(MAX)     NULL,
    CreatedOn        DATETIME2(0)      NOT NULL CONSTRAINT DF_Retry_Created DEFAULT(SYSUTCDATETIME()),
    UpdatedOn        DATETIME2(0)      NULL
);
CREATE INDEX IX_Retry_Pending ON dbo.RetryQueue(Status, NextAttemptOn) WHERE Status = 'Pending';
GO

/* ============================================================================
   SEED ROLES
   ============================================================================ */
INSERT INTO dbo.Roles (RoleName, Description) VALUES
    ('Admin',    'Full system access'),
    ('Manager',  'Manage products and mappings'),
    ('Operator', 'Run automation only');
GO

PRINT 'Schema created. 9 tables + Roles + UserRoles ready.';
GO
