using JOIN.Domain.Customers.Teams;
using JOIN.Persistence.Configuration.Customers.Leads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Customers.Teams;

/// <summary>Configures <c>Customer.TeamRoles</c> (SPEC 60).</summary>
public class TeamRoleConfiguration : IEntityTypeConfiguration<TeamRole>
{
    public void Configure(EntityTypeBuilder<TeamRole> builder) =>
        CommercialCatalogConfiguration.ConfigureCatalog(builder, "TeamRoles");
}

/// <summary>Configures <c>Customer.SalesTeams</c> (SPEC 60).</summary>
public class SalesTeamConfiguration : IEntityTypeConfiguration<SalesTeam>
{
    public void Configure(EntityTypeBuilder<SalesTeam> builder)
    {
        builder.ToTable("SalesTeams", "Customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.IsActive).IsRequired();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique().HasFilter("\"gcrecord\" = 0").HasDatabaseName("UX_SalesTeams_Company_Name");
    }
}

/// <summary>
/// Configures <c>Customer.SalesTeamMembers</c> (SPEC 60): one open membership per user and team, and one open primary
/// membership per user and company.
/// </summary>
public class SalesTeamMemberConfiguration : IEntityTypeConfiguration<SalesTeamMember>
{
    public void Configure(EntityTypeBuilder<SalesTeamMember> builder)
    {
        builder.ToTable("SalesTeamMembers", "Customer");
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.IsOpen);
        builder.Property(x => x.ValidFrom).HasColumnType("date");
        builder.Property(x => x.ValidTo).HasColumnType("date");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SalesTeam).WithMany().HasForeignKey(x => x.SalesTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TeamRole).WithMany().HasForeignKey(x => x.TeamRoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JOIN.Domain.Security.ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SalesTeamId, x.UserId }).IsUnique()
            .HasFilter("\"validto\" IS NULL AND \"gcrecord\" = 0").HasDatabaseName("UX_SalesTeamMembers_Team_User_Open");
        builder.HasIndex(x => new { x.CompanyId, x.UserId }).IsUnique()
            .HasFilter("\"isprimary\" = TRUE AND \"validto\" IS NULL AND \"gcrecord\" = 0").HasDatabaseName("UX_SalesTeamMembers_Company_User_Primary");
    }
}
