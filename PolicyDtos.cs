namespace TravelExpense.Api.Dtos;

public record ExpenseCategoryDto(int ExpenseCategoryId, string CategoryCode, string CategoryName, bool IsTicketCategory);

public record PolicyCapDto(
    int PolicyCapId, int RoleId, string RoleCode, string RoleName, string? ChannelName,
    int ExpenseCategoryId, string CategoryCode, string CategoryName,
    bool IsAllowed, decimal? MaxAmountPerDay, decimal? MaxAmountPerBooking, string? MaxClass,
    string Currency, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public record UpsertPolicyCapRequest(
    int RoleId, int ExpenseCategoryId, bool IsAllowed,
    decimal? MaxAmountPerDay, decimal? MaxAmountPerBooking, string? MaxClass,
    string Currency, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

/// <summary>The full, ready-to-apply policy matrix for one role - used by the expense
/// entry screen so the UI can show caps/eligibility live as the user types.</summary>
public record RolePolicyMatrixDto(int RoleId, string RoleCode, string RoleName, List<PolicyCapDto> Caps);
