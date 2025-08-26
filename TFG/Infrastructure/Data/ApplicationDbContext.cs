using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Integrations;
using Shared.Enums;
using TFG.Domain.Entities;

namespace TFG.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<User>
{
	public DbSet<User> Users { get; set; }
	public DbSet<Project> Projects { get; set; }
	public DbSet<Rol> Roles { get; set; }
	public DbSet<ApiConfiguration> ApiConfigurations { get; set; }
	public DbSet<GoRaceExperience> GoRaceExperiences { get; set; }
	public DbSet<GoRaceProjectExperience> GoRaceProjectExperiences { get; set; }
	public DbSet<GoRacePlatformExperience> GoRacePlatformExperiences { get; set; }
	public DbSet<ProjectStatusSnapshot> ProjectStatusSnapshots { get; set; }
	public DbSet<UserProjectStatusSnapshot> UserProjectStatusSnapshot { get; set; }

	public ApplicationDbContext(DbContextOptions options) : base(options)
	{
	}

	protected override void OnModelCreating(ModelBuilder builder)
	{
		builder.Entity<Rol>()
			.Property(r => r.Permissions)
			.HasConversion<int>();
		builder.Entity<Rol>().HasData(
			new Rol
			{
				Id = new Guid("d3b1f5c2-8e4a-4c0b-9f6e-7c1d2e3f4a5b"),
				Name = "Administrador",
				Permissions = Permissions.All
			},
			new Rol
			{
				Id = new Guid("85a374fa-242a-4323-8b31-32165e0b8e44"),
				Name = "Profesor",
				Permissions = Permissions.CreateProjects
			   | Permissions.EditProjects
			   | Permissions.ViewAllProjects
			   | Permissions.ViewProjectUsers
			   | Permissions.ViewProjectKpis
			   | Permissions.ViewProjectOtherUserData
			   | Permissions.CreateUsers
			   | Permissions.EditUsers
			   | Permissions.DeleteUsers
			   | Permissions.CreateRoles
			   | Permissions.EditRoles
			   | Permissions.ViewRoles
			   | Permissions.ViewUsers
			   | Permissions.CreateExperiences
			   | Permissions.EditExperiences
			   | Permissions.DeleteExperiences
			},
			new Rol
			{
				Id = new Guid("8ce70a12-67b6-451c-82b8-763c18f44a1e"),
				Name = "Alumno",
				Permissions = Permissions.None
			}
		);

		builder.Entity<GoRaceExperience>()
			.HasDiscriminator<string>("ExperienceType")
			.HasValue<GoRaceExperience>("Base")
			.HasValue<GoRaceProjectExperience>("Project")
			.HasValue<GoRacePlatformExperience>("Platform");

		// Configure the relationship between Project and User (Owner)
		builder.Entity<Project>()
			.HasOne(p => p.Owner)
			.WithMany()
			.HasForeignKey(p => p.OwnerId)
			.OnDelete(DeleteBehavior.Restrict);

		base.OnModelCreating(builder);
	}

	public static void SeedConfig(DbContext context)
	{
		var gitlabConfig = context.Set<ApiConfiguration>().FirstOrDefault(c => c.Type == ApiConfigurationType.Gitlab);
		if (gitlabConfig == null)
		{
			context.Set<ApiConfiguration>().Add(new ApiConfiguration
			{
				Type = ApiConfigurationType.Gitlab,
				Name = "Gitlab",
				BaseUrl = string.Empty,
				Token = string.Empty
			});
		}

		var openProjectConfig = context.Set<ApiConfiguration>().FirstOrDefault(c => c.Type == ApiConfigurationType.OpenProject);
		if (openProjectConfig == null)
		{
			context.Set<ApiConfiguration>().Add(new ApiConfiguration
			{
				Type = ApiConfigurationType.OpenProject,
				Name = "OpenProject",
				BaseUrl = string.Empty,
				Token = string.Empty
			});
		}

		var sonarQubeConfig = context.Set<ApiConfiguration>().FirstOrDefault(c => c.Type == ApiConfigurationType.SonarQube);
		if (sonarQubeConfig == null)
		{
			context.Set<ApiConfiguration>().Add(new ApiConfiguration
			{
				Type = ApiConfigurationType.SonarQube,
				Name = "SonarQube",
				BaseUrl = string.Empty,
				Token = string.Empty
			});
		}
		context.SaveChanges();
	}

	public static async Task SeedConfigAsync(DbContext context)
	{
		var gitlabConfig = await context.Set<ApiConfiguration>().FirstOrDefaultAsync(c => c.Type == ApiConfigurationType.Gitlab);
		if (gitlabConfig == null)
		{
			context.Set<ApiConfiguration>().Add(new ApiConfiguration
			{
				Type = ApiConfigurationType.Gitlab,
				Name = "Gitlab",
				BaseUrl = "http://localhost",
				Token = string.Empty
			});
		}

		var openProjectConfig = context.Set<ApiConfiguration>().FirstOrDefaultAsync(c => c.Type == ApiConfigurationType.OpenProject);
		if (openProjectConfig == null)
		{
			context.Set<ApiConfiguration>().Add(new ApiConfiguration
			{
				Type = ApiConfigurationType.OpenProject,
				Name = "OpenProject",
				BaseUrl = "http://localhost",
				Token = string.Empty
			});
		}

		var sonarQubeConfig = context.Set<ApiConfiguration>().FirstOrDefaultAsync(c => c.Type == ApiConfigurationType.SonarQube);
		if (sonarQubeConfig == null)
		{
			context.Set<ApiConfiguration>().Add(new ApiConfiguration
			{
				Type = ApiConfigurationType.SonarQube,
				Name = "SonarQube",
				BaseUrl = "http://localhost",
				Token = string.Empty
			});
		}
		await context.SaveChangesAsync();
	}
}
