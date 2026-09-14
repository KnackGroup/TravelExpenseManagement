/* ==========================================================================
   Sales Travel & Expense Management System
   SQL Server schema (source of truth for the data model)

   Design notes
   ------------
   - Hierarchy (Channel -> Role -> Employee) is fully data-driven. Nothing
     about "GM / Regional Manager / Area Manager" or "Continent Manager" is
     hard-coded in application logic: it all lives in Channels and Roles,
     and can be extended (new channel, new level, renamed role) purely by
     changing data through the admin API.
   - Per-role, per-expense-category spending caps AND flight/train
     eligibility both live in one table (PolicyCaps) so the whole travel
     policy is configurable without a deployment. Caps are date-ranged
     (EffectiveFrom/EffectiveTo) so policy history is preserved.
   - Every workflow status is a small lookup-free string column with a
     CHECK constraint (kept intentionally simple/readable rather than
     normalized into more lookup tables).
   ========================================================================== */

IF DB_ID('TravelExpenseDb') IS NULL
BEGIN
    CREATE DATABASE TravelExpenseDb;
END
GO

USE TravelExpenseDb;
GO

/* ---------------------------------------------------------------------
   1. Channels  (Domestic, Export, ... extensible)
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Channels', 'U') IS NULL
CREATE TABLE dbo.Channels
(
    ChannelId       INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Channels PRIMARY KEY,
    ChannelCode     NVARCHAR(20)        NOT NULL,
    ChannelName     NVARCHAR(100)       NOT NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Channels_IsActive DEFAULT (1),
    CreatedAt       DATETIME2           NOT NULL CONSTRAINT DF_Channels_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Channels_Code UNIQUE (ChannelCode)
);
GO

/* ---------------------------------------------------------------------
   2. Roles - the dynamic hierarchy definition.
      HierarchyLevel 1 = top of the sales chain (General Manager).
      ChannelId is NULL for cross-channel roles (Accounts, Admin).
      RoleType routes workflow: 'Sales' roles sit in the tour/advance/
      expense chain, 'Accounts' roles do the finance approval + payout
      step, 'Admin' roles configure hierarchy/policy.
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Roles', 'U') IS NULL
CREATE TABLE dbo.Roles
(
    RoleId          INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    ChannelId       INT                 NULL CONSTRAINT FK_Roles_Channel REFERENCES dbo.Channels(ChannelId),
    RoleCode        NVARCHAR(30)        NOT NULL,
    RoleName        NVARCHAR(100)       NOT NULL,
    HierarchyLevel  INT                 NULL,               -- 1 = top; NULL for non-sales roles
    RoleType        NVARCHAR(20)        NOT NULL CONSTRAINT DF_Roles_Type DEFAULT ('Sales')
                        CONSTRAINT CK_Roles_Type CHECK (RoleType IN ('Sales','Accounts','Admin')),
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Roles_IsActive DEFAULT (1),
    CreatedAt       DATETIME2           NOT NULL CONSTRAINT DF_Roles_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Roles_Channel_Code UNIQUE (ChannelId, RoleCode)
);
GO

/* ---------------------------------------------------------------------
   3. Employees - also doubles as the login/user table.
      ManagerEmployeeId is the ACTUAL reporting line used to route
      approvals (independent of RoleId, so re-orgs don't require schema
      changes - just re-pointing a manager).
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Employees', 'U') IS NULL
CREATE TABLE dbo.Employees
(
    EmployeeId          INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    EmployeeCode         NVARCHAR(30)       NOT NULL,
    FullName             NVARCHAR(150)      NOT NULL,
    Email                NVARCHAR(200)      NOT NULL,
    PasswordHash         NVARCHAR(300)      NOT NULL,
    RoleId               INT                NOT NULL CONSTRAINT FK_Employees_Role REFERENCES dbo.Roles(RoleId),
    ManagerEmployeeId    INT                NULL CONSTRAINT FK_Employees_Manager REFERENCES dbo.Employees(EmployeeId),
    IsActive             BIT                NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT (1),
    CreatedAt            DATETIME2          NOT NULL CONSTRAINT DF_Employees_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt             DATETIME2         NOT NULL CONSTRAINT DF_Employees_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Employees_Code UNIQUE (EmployeeCode),
    CONSTRAINT UQ_Employees_Email UNIQUE (Email)
);
GO

/* ---------------------------------------------------------------------
   4. Expense categories (Food, Hotel, Local Conveyance, Flight, Train, ...)
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.ExpenseCategories', 'U') IS NULL
CREATE TABLE dbo.ExpenseCategories
(
    ExpenseCategoryId   INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_ExpenseCategories PRIMARY KEY,
    CategoryCode        NVARCHAR(30)       NOT NULL,
    CategoryName        NVARCHAR(100)      NOT NULL,
    IsTicketCategory     BIT               NOT NULL CONSTRAINT DF_ExpCat_IsTicket DEFAULT (0), -- Flight / Train
    IsActive             BIT               NOT NULL CONSTRAINT DF_ExpCat_IsActive DEFAULT (1),
    CONSTRAINT UQ_ExpenseCategories_Code UNIQUE (CategoryCode)
);
GO

/* ---------------------------------------------------------------------
   5. PolicyCaps - the whole travel policy: per role, per expense
      category, per day cap + per-booking cap + whether the category is
      allowed at all (this is how "Area Manager cannot book flights" is
      expressed - a row with IsAllowed = 0). Date-ranged so history of
      policy changes is preserved instead of overwritten.
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.PolicyCaps', 'U') IS NULL
CREATE TABLE dbo.PolicyCaps
(
    PolicyCapId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PolicyCaps PRIMARY KEY,
    RoleId                INT              NOT NULL CONSTRAINT FK_PolicyCaps_Role REFERENCES dbo.Roles(RoleId),
    ExpenseCategoryId     INT              NOT NULL CONSTRAINT FK_PolicyCaps_Category REFERENCES dbo.ExpenseCategories(ExpenseCategoryId),
    IsAllowed             BIT              NOT NULL CONSTRAINT DF_PolicyCaps_IsAllowed DEFAULT (1),
    MaxAmountPerDay       DECIMAL(12,2)    NULL,             -- e.g. food/hotel/local conveyance per-day cap
    MaxAmountPerBooking   DECIMAL(12,2)    NULL,             -- e.g. flight/train per-ticket cap
    MaxClass              NVARCHAR(50)     NULL,             -- e.g. 'Economy', 'AC-2Tier', 'Business'
    Currency               CHAR(3)         NOT NULL CONSTRAINT DF_PolicyCaps_Currency DEFAULT ('INR'),
    EffectiveFrom          DATE            NOT NULL CONSTRAINT DF_PolicyCaps_From DEFAULT (CAST(SYSUTCDATETIME() AS DATE)),
    EffectiveTo             DATE           NULL,
    CreatedByEmployeeId    INT             NULL CONSTRAINT FK_PolicyCaps_CreatedBy REFERENCES dbo.Employees(EmployeeId),
    CreatedAt               DATETIME2      NOT NULL CONSTRAINT DF_PolicyCaps_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt                DATETIME2     NOT NULL CONSTRAINT DF_PolicyCaps_UpdatedAt DEFAULT (SYSUTCDATETIME())
);
GO
CREATE INDEX IX_PolicyCaps_Role_Category_Effective ON dbo.PolicyCaps (RoleId, ExpenseCategoryId, EffectiveFrom, EffectiveTo);
GO

/* ---------------------------------------------------------------------
   6. Tour plans + stops
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.TourPlans', 'U') IS NULL
CREATE TABLE dbo.TourPlans
(
    TourPlanId       INT IDENTITY(1,1)    NOT NULL CONSTRAINT PK_TourPlans PRIMARY KEY,
    EmployeeId        INT                 NOT NULL CONSTRAINT FK_TourPlans_Employee REFERENCES dbo.Employees(EmployeeId),
    ChannelId          INT                NOT NULL CONSTRAINT FK_TourPlans_Channel REFERENCES dbo.Channels(ChannelId),
    Title               NVARCHAR(200)     NOT NULL,
    PurposeOfVisit       NVARCHAR(500)    NULL,
    StartDate             DATE            NOT NULL,
    EndDate                DATE           NOT NULL,
    Status                  NVARCHAR(30)  NOT NULL CONSTRAINT DF_TourPlans_Status DEFAULT ('Submitted')
                                CONSTRAINT CK_TourPlans_Status CHECK (Status IN ('Draft','Submitted','Approved','Rejected','Closed')),
    CreatedAt                 DATETIME2   NOT NULL CONSTRAINT DF_TourPlans_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt                  DATETIME2  NOT NULL CONSTRAINT DF_TourPlans_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_TourPlans_Dates CHECK (EndDate >= StartDate)
);
GO

IF OBJECT_ID('dbo.TourPlanStops', 'U') IS NULL
CREATE TABLE dbo.TourPlanStops
(
    TourPlanStopId    INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_TourPlanStops PRIMARY KEY,
    TourPlanId         INT                NOT NULL CONSTRAINT FK_TourPlanStops_TourPlan REFERENCES dbo.TourPlans(TourPlanId) ON DELETE CASCADE,
    VisitDate            DATE             NOT NULL,
    Location              NVARCHAR(200)   NOT NULL,
    StateOrCountry          NVARCHAR(100) NULL,
    PurposeNotes             NVARCHAR(500) NULL
);
GO
CREATE INDEX IX_TourPlanStops_TourPlan ON dbo.TourPlanStops(TourPlanId);
GO

/* ---------------------------------------------------------------------
   7. Advance requests + multi-stage approvals + disbursement
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.AdvanceRequests', 'U') IS NULL
CREATE TABLE dbo.AdvanceRequests
(
    AdvanceRequestId   INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_AdvanceRequests PRIMARY KEY,
    TourPlanId          INT               NOT NULL CONSTRAINT FK_AdvanceRequests_TourPlan REFERENCES dbo.TourPlans(TourPlanId),
    EmployeeId            INT             NOT NULL CONSTRAINT FK_AdvanceRequests_Employee REFERENCES dbo.Employees(EmployeeId),
    RequestedAmount         DECIMAL(12,2) NOT NULL,
    Currency                  CHAR(3)     NOT NULL CONSTRAINT DF_AdvanceRequests_Currency DEFAULT ('INR'),
    RequestNotes                NVARCHAR(500) NULL,
    Status                        NVARCHAR(30) NOT NULL CONSTRAINT DF_AdvanceRequests_Status DEFAULT ('Pending')
                                    CONSTRAINT CK_AdvanceRequests_Status CHECK (Status IN
                                        ('Pending','ApprovedBySuperior','RejectedBySuperior','ApprovedByAccounts','RejectedByAccounts','Disbursed')),
    CreatedAt                       DATETIME2    NOT NULL CONSTRAINT DF_AdvanceRequests_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt                        DATETIME2   NOT NULL CONSTRAINT DF_AdvanceRequests_UpdatedAt DEFAULT (SYSUTCDATETIME())
);
GO
CREATE INDEX IX_AdvanceRequests_TourPlan ON dbo.AdvanceRequests(TourPlanId);
CREATE INDEX IX_AdvanceRequests_Employee ON dbo.AdvanceRequests(EmployeeId);
GO

IF OBJECT_ID('dbo.AdvanceApprovals', 'U') IS NULL
CREATE TABLE dbo.AdvanceApprovals
(
    AdvanceApprovalId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdvanceApprovals PRIMARY KEY,
    AdvanceRequestId      INT             NOT NULL CONSTRAINT FK_AdvanceApprovals_Request REFERENCES dbo.AdvanceRequests(AdvanceRequestId),
    ApproverEmployeeId      INT           NOT NULL CONSTRAINT FK_AdvanceApprovals_Approver REFERENCES dbo.Employees(EmployeeId),
    ApprovalStage             NVARCHAR(20) NOT NULL CONSTRAINT CK_AdvanceApprovals_Stage CHECK (ApprovalStage IN ('Superior','Accounts')),
    Decision                    NVARCHAR(20) NOT NULL CONSTRAINT CK_AdvanceApprovals_Decision CHECK (Decision IN ('Approved','Rejected')),
    ApprovedAmount                 DECIMAL(12,2) NULL,
    Comments                         NVARCHAR(500) NULL,
    DecidedAt                          DATETIME2  NOT NULL CONSTRAINT DF_AdvanceApprovals_DecidedAt DEFAULT (SYSUTCDATETIME())
);
GO
CREATE INDEX IX_AdvanceApprovals_Request ON dbo.AdvanceApprovals(AdvanceRequestId);
GO

IF OBJECT_ID('dbo.AdvanceDisbursements', 'U') IS NULL
CREATE TABLE dbo.AdvanceDisbursements
(
    AdvanceDisbursementId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdvanceDisbursements PRIMARY KEY,
    AdvanceRequestId         INT             NOT NULL CONSTRAINT FK_AdvanceDisbursements_Request REFERENCES dbo.AdvanceRequests(AdvanceRequestId),
    DisbursementMode           NVARCHAR(20) NOT NULL CONSTRAINT CK_AdvanceDisbursements_Mode CHECK (DisbursementMode IN ('Cash','BankTransfer')),
    Amount                        DECIMAL(12,2) NOT NULL,
    ReferenceNo                     NVARCHAR(100) NULL,
    DisbursedByEmployeeId              INT   NOT NULL CONSTRAINT FK_AdvanceDisbursements_By REFERENCES dbo.Employees(EmployeeId),
    DisbursedAt                          DATETIME2 NOT NULL CONSTRAINT DF_AdvanceDisbursements_At DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_AdvanceDisbursements_Request UNIQUE (AdvanceRequestId)
);
GO

/* ---------------------------------------------------------------------
   8. Expense reports + line items (policy-variance-aware)
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.ExpenseReports', 'U') IS NULL
CREATE TABLE dbo.ExpenseReports
(
    ExpenseReportId    INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_ExpenseReports PRIMARY KEY,
    TourPlanId          INT               NOT NULL CONSTRAINT FK_ExpenseReports_TourPlan REFERENCES dbo.TourPlans(TourPlanId),
    EmployeeId            INT             NOT NULL CONSTRAINT FK_ExpenseReports_Employee REFERENCES dbo.Employees(EmployeeId),
    SubmittedAt             DATETIME2     NULL,
    Status                    NVARCHAR(30) NOT NULL CONSTRAINT DF_ExpenseReports_Status DEFAULT ('Draft')
                                CONSTRAINT CK_ExpenseReports_Status CHECK (Status IN
                                    ('Draft','Submitted','UnderReview','Approved','PartiallyApproved','Rejected')),
    TotalClaimedAmount           DECIMAL(12,2) NOT NULL CONSTRAINT DF_ExpenseReports_Claimed DEFAULT (0),
    TotalApprovedAmount            DECIMAL(12,2) NULL,
    HasPolicyExceptions               BIT     NOT NULL CONSTRAINT DF_ExpenseReports_HasExceptions DEFAULT (0),
    CreatedAt                           DATETIME2 NOT NULL CONSTRAINT DF_ExpenseReports_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt                             DATETIME2 NOT NULL CONSTRAINT DF_ExpenseReports_UpdatedAt DEFAULT (SYSUTCDATETIME())
);
GO
CREATE INDEX IX_ExpenseReports_TourPlan ON dbo.ExpenseReports(TourPlanId);
CREATE INDEX IX_ExpenseReports_Employee ON dbo.ExpenseReports(EmployeeId);
GO

IF OBJECT_ID('dbo.ExpenseLineItems', 'U') IS NULL
CREATE TABLE dbo.ExpenseLineItems
(
    ExpenseLineItemId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpenseLineItems PRIMARY KEY,
    ExpenseReportId       INT             NOT NULL CONSTRAINT FK_ExpenseLineItems_Report REFERENCES dbo.ExpenseReports(ExpenseReportId) ON DELETE CASCADE,
    ExpenseDate             DATE          NOT NULL,
    ExpenseCategoryId         INT         NOT NULL CONSTRAINT FK_ExpenseLineItems_Category REFERENCES dbo.ExpenseCategories(ExpenseCategoryId),
    Description                 NVARCHAR(300) NULL,
    ClaimedAmount                 DECIMAL(12,2) NOT NULL,
    Currency                        CHAR(3)  NOT NULL CONSTRAINT DF_ExpenseLineItems_Currency DEFAULT ('INR'),
    PolicyCapAmountApplied            DECIMAL(12,2) NULL,   -- snapshot of the cap that was checked against, for audit
    IsPolicyException                    BIT   NOT NULL CONSTRAINT DF_ExpenseLineItems_IsException DEFAULT (0),
    ExceptionReason                        NVARCHAR(300) NULL,
    LineStatus                                NVARCHAR(20) NOT NULL CONSTRAINT DF_ExpenseLineItems_Status DEFAULT ('Pending')
                                                CONSTRAINT CK_ExpenseLineItems_Status CHECK (LineStatus IN ('Pending','Approved','Rejected')),
    ApprovedAmount                               DECIMAL(12,2) NULL,
    ApproverEmployeeId                             INT NULL CONSTRAINT FK_ExpenseLineItems_Approver REFERENCES dbo.Employees(EmployeeId),
    ApproverComments                                 NVARCHAR(300) NULL,
    DecidedAt                                          DATETIME2 NULL,
    CreatedAt                                            DATETIME2 NOT NULL CONSTRAINT DF_ExpenseLineItems_CreatedAt DEFAULT (SYSUTCDATETIME())
);
GO
CREATE INDEX IX_ExpenseLineItems_Report ON dbo.ExpenseLineItems(ExpenseReportId);
CREATE INDEX IX_ExpenseLineItems_Exceptions ON dbo.ExpenseLineItems(IsPolicyException, LineStatus);
GO

/* ---------------------------------------------------------------------
   9. Daily tour reports (field summary submitted each day of the trip)
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DailyTourReports', 'U') IS NULL
CREATE TABLE dbo.DailyTourReports
(
    DailyTourReportId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DailyTourReports PRIMARY KEY,
    TourPlanId            INT             NOT NULL CONSTRAINT FK_DailyTourReports_TourPlan REFERENCES dbo.TourPlans(TourPlanId),
    EmployeeId              INT           NOT NULL CONSTRAINT FK_DailyTourReports_Employee REFERENCES dbo.Employees(EmployeeId),
    ReportDate                DATE        NOT NULL,
    VisitedLocations             NVARCHAR(300) NULL,
    WorkSummary                    NVARCHAR(1000) NULL,
    OutcomeSummary                    NVARCHAR(1000) NULL,
    NextDayPlan                          NVARCHAR(500) NULL,
    CreatedAt                               DATETIME2 NOT NULL CONSTRAINT DF_DailyTourReports_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_DailyTourReports_Plan_Date UNIQUE (TourPlanId, ReportDate)
);
GO

/* ---------------------------------------------------------------------
   10. Reconciliation views
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.vw_TripFinancialSummary', 'V') IS NOT NULL
    DROP VIEW dbo.vw_TripFinancialSummary;
GO
CREATE VIEW dbo.vw_TripFinancialSummary AS
SELECT
    tp.TourPlanId,
    tp.EmployeeId,
    e.FullName                                     AS EmployeeName,
    r.RoleName,
    c.ChannelName,
    tp.Title,
    tp.StartDate,
    tp.EndDate,
    tp.Status                                      AS TourPlanStatus,
    ISNULL(adv.TotalAdvanceDisbursed, 0)            AS TotalAdvanceDisbursed,
    ISNULL(exp.TotalClaimed, 0)                     AS TotalExpenseClaimed,
    ISNULL(exp.TotalApproved, 0)                    AS TotalExpenseApproved,
    CAST(ISNULL(exp.HasAnyException, 0) AS BIT)     AS HasPolicyExceptions,
    ISNULL(exp.TotalApproved, 0) - ISNULL(adv.TotalAdvanceDisbursed, 0)  AS SettlementAmount, -- >0 = pay employee more, <0 = employee owes refund
    dtr.DailyReportsSubmitted,
    dtr.LastOutcomeSummary
FROM dbo.TourPlans tp
JOIN dbo.Employees e ON e.EmployeeId = tp.EmployeeId
JOIN dbo.Roles r ON r.RoleId = e.RoleId
JOIN dbo.Channels c ON c.ChannelId = tp.ChannelId
OUTER APPLY (
    SELECT SUM(d.Amount) AS TotalAdvanceDisbursed
    FROM dbo.AdvanceRequests ar
    JOIN dbo.AdvanceDisbursements d ON d.AdvanceRequestId = ar.AdvanceRequestId
    WHERE ar.TourPlanId = tp.TourPlanId
) adv
OUTER APPLY (
    SELECT
        SUM(er.TotalClaimedAmount)                                    AS TotalClaimed,
        SUM(ISNULL(er.TotalApprovedAmount, 0))                        AS TotalApproved,
        MAX(CASE WHEN er.HasPolicyExceptions = 1 THEN 1 ELSE 0 END)   AS HasAnyException
    FROM dbo.ExpenseReports er
    WHERE er.TourPlanId = tp.TourPlanId
) exp
OUTER APPLY (
    SELECT
        COUNT(*)                                                     AS DailyReportsSubmitted,
        MAX(dr2.OutcomeSummary)                                      AS LastOutcomeSummary
    FROM dbo.DailyTourReports dr2
    WHERE dr2.TourPlanId = tp.TourPlanId
) dtr;
GO

IF OBJECT_ID('dbo.vw_PendingPolicyExceptions', 'V') IS NOT NULL
    DROP VIEW dbo.vw_PendingPolicyExceptions;
GO
CREATE VIEW dbo.vw_PendingPolicyExceptions AS
SELECT
    eli.ExpenseLineItemId,
    eli.ExpenseReportId,
    er.TourPlanId,
    er.EmployeeId,
    e.FullName          AS EmployeeName,
    ec.CategoryName,
    eli.ExpenseDate,
    eli.ClaimedAmount,
    eli.PolicyCapAmountApplied,
    eli.ExceptionReason,
    eli.LineStatus
FROM dbo.ExpenseLineItems eli
JOIN dbo.ExpenseReports er ON er.ExpenseReportId = eli.ExpenseReportId
JOIN dbo.Employees e ON e.EmployeeId = er.EmployeeId
JOIN dbo.ExpenseCategories ec ON ec.ExpenseCategoryId = eli.ExpenseCategoryId
WHERE eli.IsPolicyException = 1 AND eli.LineStatus = 'Pending';
GO

/* ==========================================================================
   11. Notifications - dynamic SMTP config, editable templates per workflow
       event, per-employee opt-out, and a durable outbox queue (so sending
       is retry-safe and never blocks the request that triggered it).
   ========================================================================== */
IF OBJECT_ID('dbo.EmailSettings', 'U') IS NULL
CREATE TABLE dbo.EmailSettings
(
    EmailSettingsId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailSettings PRIMARY KEY,
    IsEnabled               BIT             NOT NULL CONSTRAINT DF_EmailSettings_Enabled DEFAULT (0), -- master on/off switch
    SmtpHost                 NVARCHAR(200)  NOT NULL CONSTRAINT DF_EmailSettings_Host DEFAULT ('smtp.office365.com'),
    SmtpPort                   INT           NOT NULL CONSTRAINT DF_EmailSettings_Port DEFAULT (587),
    EnableSsl                    BIT         NOT NULL CONSTRAINT DF_EmailSettings_Ssl DEFAULT (1),
    AuthMode                       NVARCHAR(20) NOT NULL CONSTRAINT DF_EmailSettings_AuthMode DEFAULT ('Basic')
                                        CONSTRAINT CK_EmailSettings_AuthMode CHECK (AuthMode IN ('Basic','OAuth2')),
    SmtpUsername                     NVARCHAR(200) NULL,          -- mailbox / app registration client id
    SmtpSecretEncrypted                NVARCHAR(1000) NULL,       -- app password (Basic) or client secret (OAuth2) - encrypted at rest
    OAuthTenantId                        NVARCHAR(200) NULL,      -- Azure AD tenant id (OAuth2 mode only)
    FromAddress                            NVARCHAR(200) NOT NULL CONSTRAINT DF_EmailSettings_From DEFAULT ('noreply@example.com'),
    FromName                                 NVARCHAR(200) NOT NULL CONSTRAINT DF_EmailSettings_FromName DEFAULT ('Travel & Expense System'),
    UpdatedByEmployeeId                        INT NULL CONSTRAINT FK_EmailSettings_UpdatedBy REFERENCES dbo.Employees(EmployeeId),
    UpdatedAt                                    DATETIME2 NOT NULL CONSTRAINT DF_EmailSettings_UpdatedAt DEFAULT (SYSUTCDATETIME())
);
GO

IF OBJECT_ID('dbo.EmailTemplates', 'U') IS NULL
CREATE TABLE dbo.EmailTemplates
(
    EmailTemplateId    INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_EmailTemplates PRIMARY KEY,
    EventType            NVARCHAR(60)      NOT NULL,
    Description             NVARCHAR(300)  NULL,
    Subject                   NVARCHAR(300) NOT NULL,
    BodyHtml                    NVARCHAR(MAX) NOT NULL,           -- supports {{PlaceholderName}} tokens, see docs/email-templates.md
    IsEnabled                     BIT       NOT NULL CONSTRAINT DF_EmailTemplates_Enabled DEFAULT (1),
    UpdatedByEmployeeId              INT    NULL CONSTRAINT FK_EmailTemplates_UpdatedBy REFERENCES dbo.Employees(EmployeeId),
    UpdatedAt                          DATETIME2 NOT NULL CONSTRAINT DF_EmailTemplates_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_EmailTemplates_EventType UNIQUE (EventType)
);
GO

IF OBJECT_ID('dbo.EmployeeNotificationPreferences', 'U') IS NULL
CREATE TABLE dbo.EmployeeNotificationPreferences
(
    EmployeeNotificationPreferenceId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeNotificationPreferences PRIMARY KEY,
    EmployeeId                         INT             NOT NULL CONSTRAINT FK_EmpNotifPref_Employee REFERENCES dbo.Employees(EmployeeId),
    EventType                            NVARCHAR(60)  NOT NULL,
    IsEnabled                              BIT          NOT NULL CONSTRAINT DF_EmpNotifPref_Enabled DEFAULT (1),
    CONSTRAINT UQ_EmpNotifPref_Employee_Event UNIQUE (EmployeeId, EventType)
);
GO

IF OBJECT_ID('dbo.EmailOutbox', 'U') IS NULL
CREATE TABLE dbo.EmailOutbox
(
    EmailOutboxId     INT IDENTITY(1,1)    NOT NULL CONSTRAINT PK_EmailOutbox PRIMARY KEY,
    ToAddress           NVARCHAR(500)      NOT NULL,             -- semicolon-separated for multiple recipients
    CcAddress             NVARCHAR(500)    NULL,
    Subject                 NVARCHAR(300)  NOT NULL,
    BodyHtml                  NVARCHAR(MAX) NOT NULL,
    EventType                   NVARCHAR(60) NOT NULL,
    RelatedEntityType             NVARCHAR(60) NULL,             -- e.g. 'AdvanceRequest', 'ExpenseReport'
    RelatedEntityId                 INT      NULL,
    Status                            NVARCHAR(20) NOT NULL CONSTRAINT DF_EmailOutbox_Status DEFAULT ('Pending')
                                        CONSTRAINT CK_EmailOutbox_Status CHECK (Status IN ('Pending','Sent','Failed','Skipped')),
    Attempts                            INT       NOT NULL CONSTRAINT DF_EmailOutbox_Attempts DEFAULT (0),
    LastError                             NVARCHAR(1000) NULL,
    CreatedAt                               DATETIME2 NOT NULL CONSTRAINT DF_EmailOutbox_CreatedAt DEFAULT (SYSUTCDATETIME()),
    SentAt                                    DATETIME2 NULL
);
GO
CREATE INDEX IX_EmailOutbox_Status ON dbo.EmailOutbox(Status, CreatedAt);
GO

/* ==========================================================================
   12. AI provider configuration - dynamic, admin-editable, same "bring your
       own key" pattern as email. When IsEnabled = 0 (default), the AI-lite
       features (risk scoring, anomaly detection, advance suggestions,
       policy Q&A) still work fully - they're computed from this database,
       not from an external model. This row only gates the LLM-backed
       features (receipt OCR, natural-language tour plan entry, AI trip
       summaries) that need an external provider.
   ========================================================================== */
IF OBJECT_ID('dbo.AiSettings', 'U') IS NULL
CREATE TABLE dbo.AiSettings
(
    AiSettingsId        INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_AiSettings PRIMARY KEY,
    IsEnabled              BIT              NOT NULL CONSTRAINT DF_AiSettings_Enabled DEFAULT (0),
    Provider                 NVARCHAR(30)   NOT NULL CONSTRAINT DF_AiSettings_Provider DEFAULT ('None')
                                CONSTRAINT CK_AiSettings_Provider CHECK (Provider IN ('None','OpenAI','AzureOpenAI')),
    ApiEndpoint                 NVARCHAR(500) NULL,               -- required for AzureOpenAI; optional override for OpenAI
    ApiKeyEncrypted                NVARCHAR(1000) NULL,
    Model                             NVARCHAR(100) NULL,          -- e.g. 'gpt-4o-mini', or an Azure deployment name
    UpdatedByEmployeeId                 INT NULL CONSTRAINT FK_AiSettings_UpdatedBy REFERENCES dbo.Employees(EmployeeId),
    UpdatedAt                             DATETIME2 NOT NULL CONSTRAINT DF_AiSettings_UpdatedAt DEFAULT (SYSUTCDATETIME())
);
GO
