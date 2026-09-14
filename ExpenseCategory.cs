namespace TravelExpense.Api.Data.Entities;

public class ExpenseCategory
{
    public int ExpenseCategoryId { get; set; }
    public string CategoryCode { get; set; } = default!;
    public string CategoryName { get; set; } = default!;
    public bool IsTicketCategory { get; set; }
    public bool IsActive { get; set; } = true;
}
