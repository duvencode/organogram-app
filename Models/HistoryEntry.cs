namespace OrganogramApp.Models;

public class HistoryEntry
{
    public DateTime Timestamp { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? Position { get; set; }
    public string Event { get; set; } = "";
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
