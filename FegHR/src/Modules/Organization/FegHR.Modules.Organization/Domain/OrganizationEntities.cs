using FegHR.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FegHR.Modules.Organization.Domain;

/// <summary>A named setup row: departments and designations share this shape.</summary>
internal abstract class NamedEntity : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary><c>org.Departments</c>.</summary>
internal sealed class Department : NamedEntity;

/// <summary><c>org.Designations</c>.</summary>
internal sealed class Designation : NamedEntity;

/// <summary>
/// <c>org.Employees</c>. <see cref="BranchId"/> refers to a platform branch and
/// <see cref="UserId"/> to the person's own sign-in, both by id only.
/// </summary>
internal sealed class Employee : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly? JoiningDate { get; set; }
    public DateOnly? ExitDate { get; set; }
    public string? EmploymentType { get; set; }
    public int? BranchId { get; set; }
    public int? DepartmentId { get; set; }
    public int? DesignationId { get; set; }
    public int? ReportsToId { get; set; }
    public int? UserId { get; set; }
    public string? PhotoKey { get; set; }
    public bool IsActive { get; set; } = true;
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("Departments");
        b.Property(x => x.Name).HasMaxLength(150);
    }
}

internal sealed class DesignationConfiguration : IEntityTypeConfiguration<Designation>
{
    public void Configure(EntityTypeBuilder<Designation> b)
    {
        b.ToTable("Designations");
        b.Property(x => x.Name).HasMaxLength(150);
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("Employees");
        b.Property(x => x.Code).HasMaxLength(50);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.Gender).HasMaxLength(20);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.EmploymentType).HasMaxLength(50);
        b.Property(x => x.PhotoKey).HasMaxLength(400);
    }
}
