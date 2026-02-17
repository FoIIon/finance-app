using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class DashboardDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsCreator { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DashboardDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsCreator { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<DashboardMemberDto> Members { get; set; } = new();
    public List<AccountDto> Accounts { get; set; } = new();
}

public class DashboardMemberDto
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}

public class CreateDashboardDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}

public class UpdateDashboardDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}

public class AddAccountToDashboardDto
{
    [Required]
    public int AccountId { get; set; }
}
