using FegHR.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FegHR.Modules.Platform.Domain;

/// <summary><c>platform.TenantSettings</c>: one row per company.</summary>
internal sealed class TenantSetting : TenantEntity
{
    public string CurrencyCode { get; set; } = "BDT";
    public string TimeZoneId { get; set; } = "Asia/Dhaka";
    public bool IsActive { get; set; } = true;
    /// <summary>Versioned JSON of <see cref="Features.Branding.BrandingPayload"/>; <c>{}</c> until first saved.</summary>
    public string BrandingJson { get; set; } = "{}";
}

internal sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> b)
    {
        b.ToTable("TenantSettings");
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(x => x.TimeZoneId).HasMaxLength(100);
    }
}

/// <summary>
/// <c>platform.PlatformBranding</c>: the single row describing how the product presents itself
/// on the landing, registration and sign-in pages. Edited by the product owner only.
/// </summary>
internal sealed class PlatformBranding : IVersioned
{
    public int Id { get; set; } = 1;
    public string BrandName { get; set; } = "FegHR";
    public string NameStyleJson { get; set; } = "[]";
    public string? Tagline { get; set; }
    public string? PrimaryColour { get; set; }
    public string? AccentColour { get; set; }
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
    public string? FooterText { get; set; }
    public byte[]? LogoBytes { get; set; }
    public string? LogoContentType { get; set; }
    public byte[]? FaviconBytes { get; set; }
    public string? FaviconContentType { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class PlatformBrandingConfiguration : IEntityTypeConfiguration<PlatformBranding>
{
    public void Configure(EntityTypeBuilder<PlatformBranding> b)
    {
        b.ToTable("PlatformBranding");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.BrandName).HasMaxLength(60);
        b.Property(x => x.Tagline).HasMaxLength(200);
        b.Property(x => x.PrimaryColour).HasMaxLength(7).IsFixedLength().IsUnicode(false);
        b.Property(x => x.AccentColour).HasMaxLength(7).IsFixedLength().IsUnicode(false);
        b.Property(x => x.SupportEmail).HasMaxLength(254);
        b.Property(x => x.SupportPhone).HasMaxLength(30);
        b.Property(x => x.FooterText).HasMaxLength(300);
        b.Property(x => x.LogoContentType).HasMaxLength(30);
        b.Property(x => x.FaviconContentType).HasMaxLength(30);
    }
}

/// <summary><c>platform.Plans</c>: read-only here, for the plan pickers.</summary>
internal sealed class Plan
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public int? MaxEmployees { get; set; }
    public bool IsActive { get; set; }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("Plans");
        b.Property(x => x.MonthlyPrice).HasColumnType("decimal(18,2)");
    }
}
