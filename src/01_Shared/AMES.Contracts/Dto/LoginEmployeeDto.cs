namespace AMES.Contracts.Dto;

// Public login picker: never include hashes, roles, email or other profile fields.
public sealed record LoginEmployeeDto(string EmployeeNo, string EmployeeName);
