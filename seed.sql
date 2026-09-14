/* ==========================================================================
   Seed data
   - Domestic hierarchy: General Manager -> Regional Manager -> Area Manager
   - Export hierarchy:   General Manager -> Continent Manager -> Regional
                         Manager -> Area Manager
   - Illustrative policy caps (INR/day, INR/booking). These are SAMPLE
     numbers only - change them any time from the Admin > Policy screen,
     no redeploy needed.
   - Demo login password for every seeded user is:  Passw0rd!
     (hash below is a bcrypt hash of that string, work factor 11)
   ========================================================================== */

USE TravelExpenseDb;
GO

/* ---------------- Channels ---------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Channels WHERE ChannelCode = 'DOMESTIC')
    INSERT INTO dbo.Channels (ChannelCode, ChannelName) VALUES ('DOMESTIC', 'Domestic');
IF NOT EXISTS (SELECT 1 FROM dbo.Channels WHERE ChannelCode = 'EXPORT')
    INSERT INTO dbo.Channels (ChannelCode, ChannelName) VALUES ('EXPORT', 'Export');
GO

DECLARE @DomesticId INT = (SELECT ChannelId FROM dbo.Channels WHERE ChannelCode = 'DOMESTIC');
DECLARE @ExportId   INT = (SELECT ChannelId FROM dbo.Channels WHERE ChannelCode = 'EXPORT');

/* ---------------- Roles ---------------- */
-- Domestic: GM(1) > RM(2) > AM(3)
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE ChannelId = @DomesticId AND RoleCode = 'GM')
    INSERT INTO dbo.Roles (ChannelId, RoleCode, RoleName, HierarchyLevel, RoleType) VALUES (@DomesticId, 'GM', 'General Manager - Domestic', 1, 'Sales');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE ChannelId = @DomesticId AND RoleCode = 'RM')
    INSERT INTO dbo.Roles (ChannelId, RoleCode, RoleName, HierarchyLevel, RoleType) VALUES (@DomesticId, 'RM', 'Regional Manager - Domestic', 2, 'Sales');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE ChannelId = @DomesticId AND RoleCode = 'AM')
    INSERT INTO dbo.Roles (ChannelId, RoleCode, RoleName, HierarchyLevel, RoleType) VALUES (@DomesticId, 'AM', 'Area Manager - Domestic', 3, 'Sales');

-- Export: GM(1) > CM(2) > RM(3) > AM(4)
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE ChannelId = @ExportId AND RoleCode = 'GM')
    INSERT INTO dbo.Roles (ChannelId, RoleCode, RoleName, HierarchyLevel, RoleType) VALUES (@ExportId, 'GM', 'General Manager - Export', 1, 'Sales');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE ChannelId = @ExportId AND RoleCode = 'CM')
    INSERT INTO dbo.Roles (ChannelId, RoleCode, RoleName, HierarchyLevel, RoleType) VALUES (@ExportId, 'CM', 'Continent Manager - Export', 2, 'Sales');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE ChannelId = @ExportId AND RoleCode = 'RM')
    INSERT INTO dbo.Roles (ChannelId, RoleCode, RoleName, HierarchyLevel, RoleType) VALUES (@ExportId, 'RM', 'Regional Manager - Export', 3, 'Sales');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE ChannelId = @ExportId AND RoleCode = 'AM')
    INSERT INTO dbo.Roles (ChannelId, RoleCode, RoleName, HierarchyLevel, RoleType) VALUES (@ExportId, 'AM', 'Area Manager - Export', 4, 'Sales');

-- Cross-channel roles
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE ChannelId IS NULL AND RoleCode = 'ACCOUNTS')
    INSERT INTO dbo.Roles (ChannelId, RoleCode, RoleName, HierarchyLevel, RoleType) VALUES (NULL, 'ACCOUNTS', 'Accounts Executive', NULL, 'Accounts');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE ChannelId IS NULL AND RoleCode = 'ADMIN')
    INSERT INTO dbo.Roles (ChannelId, RoleCode, RoleName, HierarchyLevel, RoleType) VALUES (NULL, 'ADMIN', 'System Administrator', NULL, 'Admin');
GO

/* ---------------- Expense categories ---------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.ExpenseCategories WHERE CategoryCode = 'FOOD')
    INSERT INTO dbo.ExpenseCategories (CategoryCode, CategoryName, IsTicketCategory) VALUES ('FOOD', 'Food & Meals', 0);
IF NOT EXISTS (SELECT 1 FROM dbo.ExpenseCategories WHERE CategoryCode = 'HOTEL')
    INSERT INTO dbo.ExpenseCategories (CategoryCode, CategoryName, IsTicketCategory) VALUES ('HOTEL', 'Hotel / Accommodation', 0);
IF NOT EXISTS (SELECT 1 FROM dbo.ExpenseCategories WHERE CategoryCode = 'LOCAL')
    INSERT INTO dbo.ExpenseCategories (CategoryCode, CategoryName, IsTicketCategory) VALUES ('LOCAL', 'Local Conveyance / Cab', 0);
IF NOT EXISTS (SELECT 1 FROM dbo.ExpenseCategories WHERE CategoryCode = 'FLIGHT')
    INSERT INTO dbo.ExpenseCategories (CategoryCode, CategoryName, IsTicketCategory) VALUES ('FLIGHT', 'Flight Ticket', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.ExpenseCategories WHERE CategoryCode = 'TRAIN')
    INSERT INTO dbo.ExpenseCategories (CategoryCode, CategoryName, IsTicketCategory) VALUES ('TRAIN', 'Train Ticket', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.ExpenseCategories WHERE CategoryCode = 'MISC')
    INSERT INTO dbo.ExpenseCategories (CategoryCode, CategoryName, IsTicketCategory) VALUES ('MISC', 'Miscellaneous / Logistics', 0);
GO

/* ---------------- Employees (2 per role, one Accounts, one Admin) ---------------- */
DECLARE @DomesticId2 INT = (SELECT ChannelId FROM dbo.Channels WHERE ChannelCode = 'DOMESTIC');
DECLARE @ExportId2   INT = (SELECT ChannelId FROM dbo.Channels WHERE ChannelCode = 'EXPORT');
DECLARE @Hash NVARCHAR(300) = '$2b$11$josutCBmZkTMQpHZR2GDiu/HjJag9G5h7gxrinhwS0T5.uTj4PtOe'; -- bcrypt hash of: Passw0rd!

DECLARE @RoleDomGM INT = (SELECT RoleId FROM dbo.Roles WHERE ChannelId = @DomesticId2 AND RoleCode='GM');
DECLARE @RoleDomRM INT = (SELECT RoleId FROM dbo.Roles WHERE ChannelId = @DomesticId2 AND RoleCode='RM');
DECLARE @RoleDomAM INT = (SELECT RoleId FROM dbo.Roles WHERE ChannelId = @DomesticId2 AND RoleCode='AM');
DECLARE @RoleExpGM INT = (SELECT RoleId FROM dbo.Roles WHERE ChannelId = @ExportId2 AND RoleCode='GM');
DECLARE @RoleExpCM INT = (SELECT RoleId FROM dbo.Roles WHERE ChannelId = @ExportId2 AND RoleCode='CM');
DECLARE @RoleExpRM INT = (SELECT RoleId FROM dbo.Roles WHERE ChannelId = @ExportId2 AND RoleCode='RM');
DECLARE @RoleExpAM INT = (SELECT RoleId FROM dbo.Roles WHERE ChannelId = @ExportId2 AND RoleCode='AM');
DECLARE @RoleAccounts INT = (SELECT RoleId FROM dbo.Roles WHERE RoleCode='ACCOUNTS');
DECLARE @RoleAdmin INT = (SELECT RoleId FROM dbo.Roles WHERE RoleCode='ADMIN');

IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode = 'ADMIN001')
    INSERT INTO dbo.Employees (EmployeeCode, FullName, Email, PasswordHash, RoleId, ManagerEmployeeId) VALUES
        ('ADMIN001', 'System Administrator', 'admin@knackpackaging.com', @Hash, @RoleAdmin, NULL);

IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode = 'ACC001')
    INSERT INTO dbo.Employees (EmployeeCode, FullName, Email, PasswordHash, RoleId, ManagerEmployeeId) VALUES
        ('ACC001', 'Accounts - Priya Nair', 'accounts@knackpackaging.com', @Hash, @RoleAccounts, NULL);

-- Domestic chain: GM -> RM -> AM
IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode = 'DOM-GM-01')
    INSERT INTO dbo.Employees (EmployeeCode, FullName, Email, PasswordHash, RoleId, ManagerEmployeeId) VALUES
        ('DOM-GM-01', 'Rajesh Mehta (GM Domestic)', 'gm.domestic@knackpackaging.com', @Hash, @RoleDomGM, NULL);
DECLARE @DomGmId INT = (SELECT EmployeeId FROM dbo.Employees WHERE EmployeeCode='DOM-GM-01');

IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode = 'DOM-RM-01')
    INSERT INTO dbo.Employees (EmployeeCode, FullName, Email, PasswordHash, RoleId, ManagerEmployeeId) VALUES
        ('DOM-RM-01', 'Sunita Rao (RM North)', 'rm.north@knackpackaging.com', @Hash, @RoleDomRM, @DomGmId);
DECLARE @DomRmId INT = (SELECT EmployeeId FROM dbo.Employees WHERE EmployeeCode='DOM-RM-01');

IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode = 'DOM-AM-01')
    INSERT INTO dbo.Employees (EmployeeCode, FullName, Email, PasswordHash, RoleId, ManagerEmployeeId) VALUES
        ('DOM-AM-01', 'Vikram Singh (AM Delhi)', 'am.delhi@knackpackaging.com', @Hash, @RoleDomAM, @DomRmId);

-- Export chain: GM -> CM -> RM -> AM
IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode = 'EXP-GM-01')
    INSERT INTO dbo.Employees (EmployeeCode, FullName, Email, PasswordHash, RoleId, ManagerEmployeeId) VALUES
        ('EXP-GM-01', 'Anand Kulkarni (GM Export)', 'gm.export@knackpackaging.com', @Hash, @RoleExpGM, NULL);
DECLARE @ExpGmId INT = (SELECT EmployeeId FROM dbo.Employees WHERE EmployeeCode='EXP-GM-01');

IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode = 'EXP-CM-01')
    INSERT INTO dbo.Employees (EmployeeCode, FullName, Email, PasswordHash, RoleId, ManagerEmployeeId) VALUES
        ('EXP-CM-01', 'Farah Iqbal (CM Middle East & Africa)', 'cm.mea@knackpackaging.com', @Hash, @RoleExpCM, @ExpGmId);
DECLARE @ExpCmId INT = (SELECT EmployeeId FROM dbo.Employees WHERE EmployeeCode='EXP-CM-01');

IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode = 'EXP-RM-01')
    INSERT INTO dbo.Employees (EmployeeCode, FullName, Email, PasswordHash, RoleId, ManagerEmployeeId) VALUES
        ('EXP-RM-01', 'Karan Verma (RM Gulf)', 'rm.gulf@knackpackaging.com', @Hash, @RoleExpRM, @ExpCmId);
DECLARE @ExpRmId INT = (SELECT EmployeeId FROM dbo.Employees WHERE EmployeeCode='EXP-RM-01');

IF NOT EXISTS (SELECT 1 FROM dbo.Employees WHERE EmployeeCode = 'EXP-AM-01')
    INSERT INTO dbo.Employees (EmployeeCode, FullName, Email, PasswordHash, RoleId, ManagerEmployeeId) VALUES
        ('EXP-AM-01', 'Neha Joshi (AM UAE)', 'am.uae@knackpackaging.com', @Hash, @RoleExpAM, @ExpRmId);
GO

/* ---------------- Policy caps (sample - fully editable later via Admin UI) ----------------
   Interpretation of the rules described:
   - Area Manager: NOT allowed to book flights at all; train allowed with a per-booking cap.
   - Regional Manager: both flight and train allowed, higher per-day caps than AM.
   - General Manager: both flight and train allowed (higher caps / class), and local
     conveyance is capped at "cab" tier (MaxClass = 'Cab') rather than an unrestricted
     chauffeur/luxury allowance.
   - Continent Manager (export only) sits between GM and RM with its own intermediate caps.
   ------------------------------------------------------------------------------------- */
DECLARE @CatFood INT   = (SELECT ExpenseCategoryId FROM dbo.ExpenseCategories WHERE CategoryCode='FOOD');
DECLARE @CatHotel INT  = (SELECT ExpenseCategoryId FROM dbo.ExpenseCategories WHERE CategoryCode='HOTEL');
DECLARE @CatLocal INT  = (SELECT ExpenseCategoryId FROM dbo.ExpenseCategories WHERE CategoryCode='LOCAL');
DECLARE @CatFlight INT = (SELECT ExpenseCategoryId FROM dbo.ExpenseCategories WHERE CategoryCode='FLIGHT');
DECLARE @CatTrain INT  = (SELECT ExpenseCategoryId FROM dbo.ExpenseCategories WHERE CategoryCode='TRAIN');
DECLARE @CatMisc INT   = (SELECT ExpenseCategoryId FROM dbo.ExpenseCategories WHERE CategoryCode='MISC');

DECLARE @DomesticId3 INT = (SELECT ChannelId FROM dbo.Channels WHERE ChannelCode = 'DOMESTIC');
DECLARE @ExportId3   INT = (SELECT ChannelId FROM dbo.Channels WHERE ChannelCode = 'EXPORT');

-- Build one shared list of (RoleId) x each level across both channels so caps aren't duplicated by hand per row
DECLARE @Roles TABLE (RoleId INT, LevelName NVARCHAR(20));
INSERT INTO @Roles
SELECT RoleId, RoleCode FROM dbo.Roles WHERE RoleType = 'Sales';

-- Area Manager (Domestic + Export): no flight, train capped, modest caps
INSERT INTO dbo.PolicyCaps (RoleId, ExpenseCategoryId, IsAllowed, MaxAmountPerDay, MaxAmountPerBooking, MaxClass)
SELECT RoleId, @CatFood,   1, 800.00,  NULL, NULL FROM @Roles WHERE LevelName='AM'
UNION ALL SELECT RoleId, @CatHotel,  1, 2000.00, NULL, NULL FROM @Roles WHERE LevelName='AM'
UNION ALL SELECT RoleId, @CatLocal,  1, 500.00,  NULL, NULL FROM @Roles WHERE LevelName='AM'
UNION ALL SELECT RoleId, @CatFlight, 0, NULL,    NULL, NULL FROM @Roles WHERE LevelName='AM'
UNION ALL SELECT RoleId, @CatTrain,  1, NULL,    1500.00, 'AC-3Tier' FROM @Roles WHERE LevelName='AM'
UNION ALL SELECT RoleId, @CatMisc,   1, 300.00,  NULL, NULL FROM @Roles WHERE LevelName='AM';

-- Regional Manager (Domestic + Export): both flight & train allowed
INSERT INTO dbo.PolicyCaps (RoleId, ExpenseCategoryId, IsAllowed, MaxAmountPerDay, MaxAmountPerBooking, MaxClass)
SELECT RoleId, @CatFood,   1, 1200.00, NULL, NULL FROM @Roles WHERE LevelName='RM'
UNION ALL SELECT RoleId, @CatHotel,  1, 3500.00, NULL, NULL FROM @Roles WHERE LevelName='RM'
UNION ALL SELECT RoleId, @CatLocal,  1, 800.00,  NULL, NULL FROM @Roles WHERE LevelName='RM'
UNION ALL SELECT RoleId, @CatFlight, 1, NULL,    8000.00, 'Economy' FROM @Roles WHERE LevelName='RM'
UNION ALL SELECT RoleId, @CatTrain,  1, NULL,    3000.00, 'AC-2Tier' FROM @Roles WHERE LevelName='RM'
UNION ALL SELECT RoleId, @CatMisc,   1, 500.00,  NULL, NULL FROM @Roles WHERE LevelName='RM';

-- Continent Manager (Export only): between RM and GM
INSERT INTO dbo.PolicyCaps (RoleId, ExpenseCategoryId, IsAllowed, MaxAmountPerDay, MaxAmountPerBooking, MaxClass)
SELECT RoleId, @CatFood,   1, 1600.00, NULL, NULL FROM @Roles WHERE LevelName='CM'
UNION ALL SELECT RoleId, @CatHotel,  1, 4500.00, NULL, NULL FROM @Roles WHERE LevelName='CM'
UNION ALL SELECT RoleId, @CatLocal,  1, 1000.00, NULL, NULL FROM @Roles WHERE LevelName='CM'
UNION ALL SELECT RoleId, @CatFlight, 1, NULL,    15000.00, 'Economy' FROM @Roles WHERE LevelName='CM'
UNION ALL SELECT RoleId, @CatTrain,  1, NULL,    3500.00, 'AC-1Tier' FROM @Roles WHERE LevelName='CM'
UNION ALL SELECT RoleId, @CatMisc,   1, 700.00,  NULL, NULL FROM @Roles WHERE LevelName='CM';

-- General Manager (Domestic + Export): highest caps, local conveyance capped at "Cab" tier
INSERT INTO dbo.PolicyCaps (RoleId, ExpenseCategoryId, IsAllowed, MaxAmountPerDay, MaxAmountPerBooking, MaxClass)
SELECT RoleId, @CatFood,   1, 2000.00, NULL, NULL FROM @Roles WHERE LevelName='GM'
UNION ALL SELECT RoleId, @CatHotel,  1, 6000.00, NULL, NULL FROM @Roles WHERE LevelName='GM'
UNION ALL SELECT RoleId, @CatLocal,  1, 1500.00, NULL, 'Cab' FROM @Roles WHERE LevelName='GM'
UNION ALL SELECT RoleId, @CatFlight, 1, NULL,    25000.00, 'Business' FROM @Roles WHERE LevelName='GM'
UNION ALL SELECT RoleId, @CatTrain,  1, NULL,    5000.00, 'AC-1Tier' FROM @Roles WHERE LevelName='GM'
UNION ALL SELECT RoleId, @CatMisc,   1, 1000.00, NULL, NULL FROM @Roles WHERE LevelName='GM';
GO

/* ---------------- One sample tour plan + advance + expense report, end to end ---------------- */
DECLARE @AmId INT = (SELECT EmployeeId FROM dbo.Employees WHERE EmployeeCode='DOM-AM-01');
DECLARE @DomChannelId INT = (SELECT ChannelId FROM dbo.Channels WHERE ChannelCode='DOMESTIC');

IF NOT EXISTS (SELECT 1 FROM dbo.TourPlans WHERE EmployeeId = @AmId AND Title = 'Delhi-NCR Retail Coverage Aug 2026')
BEGIN
    INSERT INTO dbo.TourPlans (EmployeeId, ChannelId, Title, PurposeOfVisit, StartDate, EndDate, Status)
    VALUES (@AmId, @DomChannelId, 'Delhi-NCR Retail Coverage Aug 2026', 'Distributor visits and new retail onboarding', '2026-08-25', '2026-08-28', 'Approved');

    DECLARE @TourPlanId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.TourPlanStops (TourPlanId, VisitDate, Location, StateOrCountry, PurposeNotes) VALUES
        (@TourPlanId, '2026-08-25', 'Gurugram', 'Haryana', 'Distributor review'),
        (@TourPlanId, '2026-08-26', 'Noida', 'Uttar Pradesh', 'New retail onboarding'),
        (@TourPlanId, '2026-08-27', 'Ghaziabad', 'Uttar Pradesh', 'Collections follow-up');

    INSERT INTO dbo.AdvanceRequests (TourPlanId, EmployeeId, RequestedAmount, RequestNotes, Status)
    VALUES (@TourPlanId, @AmId, 9000.00, 'Advance for 4-day Delhi-NCR trip', 'Disbursed');

    DECLARE @AdvId INT = SCOPE_IDENTITY();
    DECLARE @RmId INT = (SELECT ManagerEmployeeId FROM dbo.Employees WHERE EmployeeId = @AmId);
    DECLARE @AccId INT = (SELECT EmployeeId FROM dbo.Employees WHERE EmployeeCode='ACC001');

    INSERT INTO dbo.AdvanceApprovals (AdvanceRequestId, ApproverEmployeeId, ApprovalStage, Decision, ApprovedAmount, Comments)
    VALUES (@AdvId, @RmId, 'Superior', 'Approved', 9000.00, 'Approved as requested'),
           (@AdvId, @AccId, 'Accounts', 'Approved', 9000.00, 'Cleared for disbursement');

    INSERT INTO dbo.AdvanceDisbursements (AdvanceRequestId, DisbursementMode, Amount, ReferenceNo, DisbursedByEmployeeId)
    VALUES (@AdvId, 'BankTransfer', 9000.00, 'NEFT-DEMO-0001', @AccId);
END
GO

/* ---------------- Email: default settings (disabled until an admin fills in real
   Outlook/M365 SMTP credentials) + default AI settings (disabled until an admin adds a key) ---------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.EmailSettings)
    INSERT INTO dbo.EmailSettings (IsEnabled, SmtpHost, SmtpPort, EnableSsl, AuthMode, FromAddress, FromName)
    VALUES (0, 'smtp.office365.com', 587, 1, 'Basic', 'noreply@knackpackaging.com', 'Knack Packaging - Travel & Expense');

IF NOT EXISTS (SELECT 1 FROM dbo.AiSettings)
    INSERT INTO dbo.AiSettings (IsEnabled, Provider) VALUES (0, 'None');

/* ---------------- Email templates - one per workflow event, all enabled by default.
   Available placeholders (not all apply to every template): {{RecipientName}},
   {{EmployeeName}}, {{ManagerName}}, {{TourPlanTitle}}, {{ChannelName}}, {{Amount}},
   {{Currency}}, {{Status}}, {{Comments}}, {{CategoryName}}, {{EventDate}}, {{AppUrl}}.
   Edit freely from Admin > Email Templates - no redeploy needed. ---------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'TourPlanSubmitted')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('TourPlanSubmitted', 'Sent to the employee''s manager when a new tour plan is submitted.',
 'New tour plan submitted: {{TourPlanTitle}}',
 '<p>Hi {{RecipientName}},</p><p><b>{{EmployeeName}}</b> has submitted a new tour plan: <b>{{TourPlanTitle}}</b> ({{ChannelName}}).</p><p><a href="{{AppUrl}}/tour-plans">View in the app</a></p>');

IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'AdvanceRequested')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('AdvanceRequested', 'Sent to the reporting manager when a direct report requests a travel advance.',
 'Advance request awaiting your approval - {{EmployeeName}}',
 '<p>Hi {{RecipientName}},</p><p><b>{{EmployeeName}}</b> has requested an advance of <b>{{Currency}} {{Amount}}</b> for "{{TourPlanTitle}}".</p><p><a href="{{AppUrl}}/advances">Review and approve</a></p>');

IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'AdvanceApprovedBySuperior')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('AdvanceApprovedBySuperior', 'Sent to Accounts once the requester''s manager approves an advance.',
 'Advance approved by manager - awaiting Accounts - {{EmployeeName}}',
 '<p>Hi {{RecipientName}},</p><p>{{EmployeeName}}''s advance request of {{Currency}} {{Amount}} for "{{TourPlanTitle}}" has been approved by their manager and is awaiting Accounts approval.</p><p><a href="{{AppUrl}}/advances">Review</a></p>');

IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'AdvanceRejected')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('AdvanceRejected', 'Sent to the employee if their advance request is rejected at either stage.',
 'Your advance request was not approved - {{TourPlanTitle}}',
 '<p>Hi {{RecipientName}},</p><p>Your advance request of {{Currency}} {{Amount}} for "{{TourPlanTitle}}" was not approved.</p><p>Comments: {{Comments}}</p><p><a href="{{AppUrl}}/advances">View details</a></p>');

IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'AdvanceDisbursed')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('AdvanceDisbursed', 'Sent to the employee once Accounts disburses the approved advance.',
 'Advance disbursed - {{Currency}} {{Amount}}',
 '<p>Hi {{RecipientName}},</p><p>Your advance of <b>{{Currency}} {{Amount}}</b> for "{{TourPlanTitle}}" has been disbursed.</p><p>Safe travels!</p>');

IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'ExpenseReportSubmitted')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('ExpenseReportSubmitted', 'Sent to the employee confirming their expense report was received.',
 'Expense report received - {{TourPlanTitle}}',
 '<p>Hi {{RecipientName}},</p><p>Your expense report for "{{TourPlanTitle}}" totalling {{Currency}} {{Amount}} has been received. Status: {{Status}}.</p><p><a href="{{AppUrl}}/expense-reports">View</a></p>');

IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'ExpensePolicyExceptionRaised')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('ExpensePolicyExceptionRaised', 'Sent to the reporting manager when a submitted expense report contains policy exceptions.',
 'Policy exception needs your decision - {{EmployeeName}}',
 '<p>Hi {{RecipientName}},</p><p>{{EmployeeName}}''s expense report for "{{TourPlanTitle}}" contains one or more items outside policy and needs your decision.</p><p><a href="{{AppUrl}}/expense-reports">Review now</a></p>');

IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'ExpenseReportDecided')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('ExpenseReportDecided', 'Sent to the employee once their expense report reaches a final decision.',
 'Your expense report was {{Status}} - {{TourPlanTitle}}',
 '<p>Hi {{RecipientName}},</p><p>Your expense report for "{{TourPlanTitle}}" is now <b>{{Status}}</b>. Approved amount: {{Currency}} {{Amount}}.</p><p><a href="{{AppUrl}}/expense-reports">View details</a></p>');

IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'DailyReportReminder')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('DailyReportReminder', 'Daily automated reminder to employees on an active trip who have not logged today''s report.',
 'Reminder: log today''s tour report - {{TourPlanTitle}}',
 '<p>Hi {{RecipientName}},</p><p>You''re on an active trip ("{{TourPlanTitle}}") and haven''t logged today''s field report yet.</p><p><a href="{{AppUrl}}/daily-reports">Log it now</a> - takes under a minute.</p>');

IF NOT EXISTS (SELECT 1 FROM dbo.EmailTemplates WHERE EventType = 'PendingApprovalDigest')
INSERT INTO dbo.EmailTemplates (EventType, Description, Subject, BodyHtml) VALUES
('PendingApprovalDigest', 'Daily automated digest to managers/Accounts listing everything awaiting their action.',
 'Daily digest: {{Amount}} item(s) awaiting your approval',
 '<p>Hi {{RecipientName}},</p><p>You have <b>{{Amount}}</b> item(s) awaiting your approval (advances and/or expense exceptions).</p><p><a href="{{AppUrl}}/">Open your dashboard</a></p>');
GO
