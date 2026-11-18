using System.Text.Json.Serialization;

namespace OrganogramApp.Models;

public class Employee
{
    [JsonPropertyName("Unique ID Number")]
    public int Id { get; set; }

    [JsonPropertyName("Employee name")]
    public string? Name { get; set; }

    [JsonPropertyName("Department")]
    public string Department { get; set; } = "";

    [JsonPropertyName("Position / Function")]
    public string? Position { get; set; }

    [JsonPropertyName("Reports to Position")]
    public string? ReportsToPosition { get; set; }

    [JsonPropertyName("Reports to Position ID")]
    public int? ReportsToId { get; set; }

    [JsonPropertyName("Job Grade")]
    public string JobGrade { get; set; } = "";

    [JsonPropertyName("Position Status")]
    public string? Status { get; set; }

    [JsonPropertyName("Card X")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CardX { get; set; }
}
