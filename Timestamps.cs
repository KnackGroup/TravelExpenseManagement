namespace TravelExpense.Api.Data.Entities;

/// <summary>
/// Implemented by any entity that has a "record created" timestamp column. AppDbContext
/// stamps it automatically in SaveChanges/SaveChangesAsync for every newly-Added entity, so
/// controllers never need to (and never need to remember to) set it by hand - the column's
/// SQL DEFAULT (SYSUTCDATETIME()) only applies when EF omits the column from the INSERT
/// entirely, which it does not do for a mapped scalar property, so without this the column
/// would otherwise always be written as the CLR default (0001-01-01).
/// </summary>
public interface ICreationTimestamped
{
    void StampCreated(DateTime utcNow);
}

/// <summary>
/// Implemented by any entity that has a "record last updated" timestamp column. AppDbContext
/// stamps it automatically for every Added or Modified entity. Controllers that already set
/// UpdatedAt explicitly before calling SaveChanges are unaffected - this just guarantees it's
/// always set, including at initial creation and on any future insert path that forgets to.
/// </summary>
public interface IUpdateTimestamped
{
    void StampUpdated(DateTime utcNow);
}
