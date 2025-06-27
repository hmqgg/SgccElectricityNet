namespace SgccElectricityNet.Worker.Models;

/// <summary>
/// Represents a daily electricity usage record.
/// </summary>
/// <param name="Date">The specific date of the record.</param>
/// <param name="Usage">The electricity usage for the day (unit: kWh).</param>
public record DailyUsageRecord(DateOnly Date, double Usage);

/// <summary>
/// Represents a monthly electricity usage and charge summary.
/// </summary>
/// <param name="Month">The specific month (e.g., "2023-05").</param>
/// <param name="Usage">The total electricity usage for the month (unit: kWh).</param>
/// <param name="Charge">The total electricity charge for the month (unit: yuan).</param>
public record MonthlyUsageRecord(string Month, double Usage, decimal Charge);

/// <summary>
/// Contains all electricity data fetched for a specific user.
/// </summary>
/// <param name="UserId">User identifier.</param>
/// <param name="Balance">Current account balance (unit: yuan). Negative if in debt.</param>
/// <param name="LastUpdateTime">Date of the most recent daily electricity usage data.</param>
/// <param name="LastDayUsage">Electricity usage for the most recent day (unit: kWh).</param>
/// <param name="CurrentYearUsage">Total electricity usage for the current year (unit: kWh).</param>
/// <param name="CurrentYearCharge">Total electricity charge for the current year (unit: yuan).</param>
/// <param name="LastMonthUsage">Total electricity usage for the current month (unit: kWh).</param>
/// <param name="LastMonthCharge">Total electricity charge for the current month (unit: yuan).</param>
/// <param name="RecentDailyUsages">List of recent (e.g., 7 days or 30 days) daily electricity usage records.</param>
/// <param name="MonthlySummaries">List of monthly electricity usage and charge summaries for the current year.</param>
public record ElectricityData(
    string UserId,
    decimal? Balance,
    DateOnly? LastUpdateTime,
    double? LastDayUsage,
    double? CurrentYearUsage,
    decimal? CurrentYearCharge,
    double? LastMonthUsage,
    decimal? LastMonthCharge,
    IReadOnlyList<DailyUsageRecord> RecentDailyUsages,
    IReadOnlyList<MonthlyUsageRecord> MonthlySummaries);
