using System.Text.Json;
using OrganogramApp.Models;

namespace OrganogramApp.Services;

public class EmployeeService(IWebHostEnvironment env)
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<Employee>? _employees;
    private List<HistoryEntry> _history = [];
    private OrganogramMeta _meta = new();
    private bool _metaLoaded;

    private string? _plan;
    public string? ActivePlan => _plan;

    private string PlansRoot => Path.Combine(env.ContentRootPath, "Plans");
    private string Dir => _plan is null ? env.ContentRootPath : Path.Combine(PlansRoot, _plan);
    private string FilePath => Path.Combine(Dir, "EmployeeData.json");
    private string MetaPath => Path.Combine(Dir, "OrganogramMeta.json");
    private string HistoryPath => Path.Combine(Dir, "EmployeeHistory.json");

    public IReadOnlyList<string> GetPlans() =>
        Directory.Exists(PlansRoot)
            ? Directory.GetDirectories(PlansRoot).Select(d => Path.GetFileName(d)!).OrderBy(n => n).ToList()
            : [];

    private void Reset()
    {
        _employees = null;
        _history = [];
        _meta = new();
        _metaLoaded = false;
    }

    public Task<List<Employee>> SwitchPlanAsync(string? name)
    {
        _plan = name;
        Reset();
        return GetEmployeesAsync();
    }

    public static string? ValidatePlanName(string name) =>
        string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Trim().EndsWith('.')
            ? null : name.Trim();

    public async Task<bool> CreatePlanAsync(string name)
    {
        var clean = ValidatePlanName(name);
        if (clean is null || GetPlans().Contains(clean, StringComparer.OrdinalIgnoreCase)) return false;
        var list = await GetEmployeesAsync();
        var dir = Path.Combine(PlansRoot, clean);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "EmployeeData.json"), JsonSerializer.Serialize(list, WriteOptions));
        await File.WriteAllTextAsync(Path.Combine(dir, "OrganogramMeta.json"), JsonSerializer.Serialize(_meta, WriteOptions));
        await File.WriteAllTextAsync(Path.Combine(dir, "EmployeeHistory.json"), "[]");
        await SwitchPlanAsync(clean);
        return true;
    }

    public async Task DeletePlanAsync(string name)
    {
        if (_plan == name) await SwitchPlanAsync(null);
        var dir = Path.Combine(PlansRoot, name);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public IReadOnlyList<string> ExtraDepartments => _meta.Departments;
    public IReadOnlyList<string> ExtraGrades => _meta.Grades.Concat(_meta.JobGrades).Distinct().ToList();
    public IReadOnlyList<string> ExtraJobGrades => _meta.JobGrades;

    public async Task<List<Employee>> GetEmployeesAsync()
    {
        if (!_metaLoaded)
        {
            if (File.Exists(MetaPath))
            {
                await using var ms = File.OpenRead(MetaPath);
                _meta = await JsonSerializer.DeserializeAsync<OrganogramMeta>(ms) ?? new();
            }
            if (File.Exists(HistoryPath))
            {
                await using var hs = File.OpenRead(HistoryPath);
                _history = await JsonSerializer.DeserializeAsync<List<HistoryEntry>>(hs) ?? [];
            }
            _metaLoaded = true;
        }
        if (_employees is not null) return _employees;
        await using var stream = File.OpenRead(FilePath);
        _employees = await JsonSerializer.DeserializeAsync<List<Employee>>(stream) ?? [];
        return _employees;
    }

    public async Task<List<HistoryEntry>> GetHistoryAsync()
    {
        await GetEmployeesAsync();
        return _history.OrderByDescending(h => h.Timestamp).ToList();
    }

    private void Log(Employee e, string ev, string field, string? oldValue, string? newValue)
    {
        _history.Add(new HistoryEntry
        {
            Timestamp = DateTime.Now,
            EmployeeId = e.Id,
            EmployeeName = e.Name,
            Position = e.Position,
            Event = ev,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    private static string? ManagerName(List<Employee> list, int? id) =>
        id is null ? null : list.FirstOrDefault(x => x.Id == id)?.Name ?? $"#{id}";

    public async Task UpdateAsync(Employee updated)
    {
        var list = await GetEmployeesAsync();
        var existing = list.FirstOrDefault(e => e.Id == updated.Id);
        if (existing is null) return;

        void Track(string field, string? oldValue, string? newValue)
        {
            if ((oldValue ?? "") != (newValue ?? "")) Log(existing, "Edited", field, oldValue, newValue);
        }

        Track("Name", existing.Name, updated.Name);
        Track("Position", existing.Position, updated.Position);
        Track("Department", existing.Department, updated.Department);
        Track("Job grade", existing.JobGrade, updated.JobGrade);
        Track("Status", existing.Status, updated.Status);
        Track("Reports to", ManagerName(list, existing.ReportsToId), ManagerName(list, updated.ReportsToId));

        existing.Name = updated.Name;
        existing.Position = updated.Position;
        existing.Department = updated.Department;
        existing.JobGrade = updated.JobGrade;
        existing.Status = updated.Status;
        existing.ReportsToId = updated.ReportsToId;
        existing.ReportsToPosition = list.FirstOrDefault(e => e.Id == updated.ReportsToId)?.Position;
        await SaveAsync(list);
    }

    public async Task SetCardXAsync(int id, int x)
    {
        var list = await GetEmployeesAsync();
        var existing = list.FirstOrDefault(e => e.Id == id);
        if (existing is null) return;
        existing.CardX = x;
        await SaveAsync(list);
    }

    public async Task SetCardsXAsync(IReadOnlyDictionary<int, int> positions)
    {
        var list = await GetEmployeesAsync();
        foreach (var e in list)
            if (positions.TryGetValue(e.Id, out var x)) e.CardX = x;
        await SaveAsync(list);
    }

    public async Task AddAsync(Employee employee)
    {
        var list = await GetEmployeesAsync();
        employee.Id = list.Count == 0 ? 1 : list.Max(e => e.Id) + 1;
        employee.ReportsToPosition = list.FirstOrDefault(e => e.Id == employee.ReportsToId)?.Position;
        employee.Status = string.IsNullOrWhiteSpace(employee.Status) ? "Filled" : employee.Status;
        list.Add(employee);
        Log(employee, "Added", "Position", null,
            $"{employee.Name} - {employee.Position} ({employee.Department}, {employee.JobGrade}, {employee.Status})");
        await SaveAsync(list);
    }

    public async Task DeleteAsync(int id)
    {
        var list = await GetEmployeesAsync();
        var target = list.FirstOrDefault(e => e.Id == id);
        if (target is null) return;

        var newManager = target.ReportsToId is int mid ? list.FirstOrDefault(e => e.Id == mid) : null;
        foreach (var child in list.Where(e => e.ReportsToId == id))
        {
            Log(child, "Manager changed", "Reports to", target.Name, newManager?.Name);
            child.ReportsToId = newManager?.Id;
            child.ReportsToPosition = newManager?.Position;
        }
        Log(target, "Deleted", "Position", $"{target.Name} - {target.Position} ({target.Department}, {target.JobGrade}, {target.Status})", null);
        list.Remove(target);
        await SaveAsync(list);
    }

    public async Task ClearAllAsync()
    {
        var list = await GetEmployeesAsync();
        list.Clear();
        _history.Clear();
        _meta = new();
        await SaveAsync(list);
    }

    public async Task AddDepartmentAsync(string name)
    {
        var list = await GetEmployeesAsync();
        if (list.Any(e => e.Department == name) || _meta.Departments.Contains(name)) return;
        _meta.Departments.Add(name);
        await SaveAsync(list);
    }

    public async Task RenameDepartmentAsync(string oldName, string newName)
    {
        var list = await GetEmployeesAsync();
        foreach (var e in list.Where(e => e.Department == oldName))
        {
            Log(e, "Department renamed", "Department", oldName, newName);
            e.Department = newName;
        }
        var i = _meta.Departments.IndexOf(oldName);
        if (i >= 0) _meta.Departments[i] = newName;
        await SaveAsync(list);
    }

    public async Task DeleteDepartmentAsync(string name)
    {
        var list = await GetEmployeesAsync();
        if (list.Any(e => e.Department == name)) return;
        _meta.Departments.Remove(name);
        await SaveAsync(list);
    }

    public async Task AddGradeAsync(string code)
    {
        var list = await GetEmployeesAsync();
        if (list.Any(e => e.JobGrade == code) || ExtraGrades.Contains(code)) return;
        _meta.Grades.Add(code);
        await SaveAsync(list);
    }

    public async Task RenameGradeAsync(string oldValue, string newValue)
    {
        var list = await GetEmployeesAsync();
        foreach (var e in list.Where(e => e.JobGrade == oldValue))
        {
            Log(e, "Skill level renamed", "Skill level", oldValue, newValue);
            e.JobGrade = newValue;
        }
        var i = _meta.Grades.IndexOf(oldValue);
        if (i >= 0) _meta.Grades[i] = newValue;
        var j = _meta.JobGrades.IndexOf(oldValue);
        if (j >= 0) _meta.JobGrades[j] = newValue;
        await SaveAsync(list);
    }

    public async Task RenameGradesAsync(IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0) return;
        var list = await GetEmployeesAsync();
        foreach (var e in list)
        {
            if (!map.TryGetValue(e.JobGrade, out var newValue)) continue;
            Log(e, "Skill level renamed", "Skill level", e.JobGrade, newValue);
            e.JobGrade = newValue;
        }
        for (var i = 0; i < _meta.Grades.Count; i++)
            if (map.TryGetValue(_meta.Grades[i], out var g)) _meta.Grades[i] = g;
        for (var j = 0; j < _meta.JobGrades.Count; j++)
            if (map.TryGetValue(_meta.JobGrades[j], out var jg)) _meta.JobGrades[j] = jg;
        _meta.Grades = _meta.Grades.Distinct().ToList();
        _meta.JobGrades = _meta.JobGrades.Distinct().ToList();
        for (var k = 0; k < _meta.GradeOrder.Count; k++)
            if (map.TryGetValue(_meta.GradeOrder[k], out var go)) _meta.GradeOrder[k] = go;
        _meta.GradeOrder = _meta.GradeOrder.Distinct().ToList();
        await SaveAsync(list);
    }

    public async Task DeleteGradeAsync(string code)
    {
        var list = await GetEmployeesAsync();
        if (list.Any(e => e.JobGrade == code)) return;
        _meta.Grades.Remove(code);
        _meta.JobGrades.Remove(code);
        _meta.GradeOrder.Remove(code);
        await SaveAsync(list);
    }

    public IReadOnlyList<string> GradeOrder => _meta.GradeOrder;

    public async Task SetGradeOrderAsync(IEnumerable<string> order)
    {
        var list = await GetEmployeesAsync();
        _meta.GradeOrder = order.Distinct().ToList();
        await SaveAsync(list);
    }

    private async Task SaveAsync(List<Employee> list)
    {
        await _lock.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(list, WriteOptions));
            await File.WriteAllTextAsync(MetaPath, JsonSerializer.Serialize(_meta, WriteOptions));
            await File.WriteAllTextAsync(HistoryPath, JsonSerializer.Serialize(_history, WriteOptions));
        }
        finally
        {
            _lock.Release();
        }
    }

    private class OrganogramMeta
    {
        public List<string> Departments { get; set; } = [];
        public List<string> Grades { get; set; } = [];
        public List<string> JobGrades { get; set; } = [];
        public List<string> GradeOrder { get; set; } = [];
    }
}
