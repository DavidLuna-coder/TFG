using MediatR;
using Microsoft.EntityFrameworkCore;
using NGitLab;
using NGitLab.Models;
using Shared.DTOs.Projects;
using System.Threading.Tasks;
using TFG.Api.Exeptions;
using TFG.Api.Mappers;
using TFG.Application.Security;
using TFG.Domain.Entities;
using TFG.Infrastructure.Data;
using TFG.Migrations;
using TFG.OpenProjectClient;
using TFG.OpenProjectClient.Models.Memberships;
using TFG.SonarQubeClient;
using TFG.SonarQubeClient.Models;
using GitlabProject = NGitLab.Models.Project;
using OPProjectCreated = TFG.OpenProjectClient.Models.Projects.ProjectCreated;
using OPProjectCreation = TFG.OpenProjectClient.Models.Projects.ProjectCreation;
using Project = TFG.Domain.Entities.Project;
using User = TFG.Domain.Entities.User;

namespace TFG.Application.Services.Projects.Commands.CreateProject;

public class CreateProjectCommandHandler(IUserInfoAccessor userInfoAccessor,
									  ApplicationDbContext dbContext,
									  IGitLabClient gitLabClient,
									  ISonarQubeClient sonarQubeClient,
									  IOpenProjectClient openProjectClient,
									  ILogger<CreateProjectCommandHandler> logger) : IRequestHandler<CreateProjectCommand, ProjectDto>
{
	public async Task<ProjectDto> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
	{
		IUserInfo userInfo = userInfoAccessor.UserInfo;
		request.UsersIds ??= new List<string>();

		var owner = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userInfo.UserId) ?? throw new NotFoundException("User does not exist");

		// Verify all users in request.UsersIds exist
		var projectUsers = await dbContext.Users.Where(u => request.UsersIds.Contains(u.Id)).ToListAsync();


		long? gitlabId = null;
		string? sonarQubeProjectKey = null;
		int? openprojectProjectId = null;

		try
		{
			var gitlabProject = await CreateAndConfigureGitlabProject(request, owner, projectUsers);
			gitlabId = gitlabProject.Id;
			sonarQubeProjectKey = request.Name.ToLowerInvariant().Replace(" ", "-");
			string sonarQubeRepositoryIdentifier = gitlabProject.Id.ToString();
			BoundedProject sonarQubeProject = await CreateAndConfigureSonarQubeProject(request, sonarQubeProjectKey, sonarQubeRepositoryIdentifier, projectUsers, owner);
			OPProjectCreated opProjectCreated = await CreateAndConfigureOpenProjectProject(request, projectUsers, owner);
			openprojectProjectId = opProjectCreated.Id;
			Project createdProject = await CreateProjectInDatabase(request, projectUsers, gitlabProject, sonarQubeProjectKey, opProjectCreated.Id, owner);

			return createdProject.ToProjectDto();
		}
		catch (Exception ex)
		{
			await CancelCreation(gitlabId, sonarQubeProjectKey, openprojectProjectId);
			throw;
		}
	}

	private async Task<Project> CreateProjectInDatabase(CreateProjectCommand projectDto, IEnumerable<User> projectUsers, GitlabProject gitlabProjectResult, string sonarQubeProjectKey, int openProjectProjectId, User owner)
	{
		//Create the project in the database
		Project newProject = new()
		{
			Id = Guid.NewGuid(),
			Name = projectDto.Name,
			Description = projectDto.Description ?? string.Empty,
			SonarQubeProjectKey = sonarQubeProjectKey,
			GitlabId = gitlabProjectResult.Id.ToString(),
			OpenProjectId = openProjectProjectId,
			Users = projectUsers.ToList(),
			CreatedAt = DateTime.UtcNow,
			Owner = owner
		};

		dbContext.Projects.Add(newProject);
		await dbContext.SaveChangesAsync();
		return newProject;
	}

	private async Task<GitlabProject> CreateAndConfigureGitlabProject(CreateProjectCommand request, User owner, IEnumerable<User> projectUsers)
	{
		ProjectCreate gitLabProject = request.ToGitlabProjectCreate();
		GitlabProject createdProject = await gitLabClient.Projects.CreateAsync(gitLabProject);

		ProjectMemberCreate ownerMember = new()
		{
			UserId = owner.GitlabId,
			AccessLevel = AccessLevel.Owner
		};
		await gitLabClient.Members.AddMemberToProjectAsync(createdProject.Id, ownerMember);

		foreach (User projectUser in projectUsers.Where(pu => pu.Id != owner.Id))
		{
			ProjectMemberCreate member = new()
			{
				UserId = projectUser.GitlabId,
				AccessLevel = AccessLevel.Developer
			};
			await gitLabClient.Members.AddMemberToProjectAsync(createdProject.Id, member);
		}

		return createdProject;
	}

	private async Task<BoundedProject> CreateAndConfigureSonarQubeProject(CreateProjectCommand projectDto, string projectKey, string repositoryIdentifier, IEnumerable<User> usersToInclude, User owner)
	{
		var dopSettings = await sonarQubeClient.DopTranslations.GetDopSettingsAsync();

		string gilabId = dopSettings.DopSettings.First(ds => ds.Type == "gitlab").Id;
		ProjectBinding projectBinding = new() { DevOpsPlatformSettingId = gilabId, ProjectKey = projectKey, ProjectName = projectDto.Name, RepositoryIdentifier = repositoryIdentifier, Monorepo = true };

		// Retry logic extraído a función
		BoundedProject project = await RetryAsync(
			() => sonarQubeClient.DopTranslations.BoundProjectAsync(projectBinding),
			new int[] { 0, 300, 1000 }
		);

		UserPermission ownerPermission = new() { Login = owner.UserName!, ProjectKey = projectKey, Permission = PermissionType.Admin };
		await sonarQubeClient.Permissions.AddUserAsync(ownerPermission);

		foreach (var user in usersToInclude.Where(uti => uti.Id != owner.Id))
		{
			UserPermission userPermission = new() { Login = user.UserName!, ProjectKey = projectKey, Permission = PermissionType.Admin };
			await sonarQubeClient.Permissions.AddUserAsync(userPermission);
		}
		return project;
	}

	private async Task<T> RetryAsync<T>(Func<Task<T>> action, int[] delays)
	{
		Exception? lastException = null;
		for (int attempt = 0; attempt < delays.Length; attempt++)
		{
			if (delays[attempt] > 0)
				await Task.Delay(delays[attempt]);
			try
			{
				return await action();
			}
			catch (Exception ex)
			{
				lastException = ex;
				if (attempt == delays.Length - 1)
					throw;
			}
		}
		throw lastException!;
	}

	private async Task<OPProjectCreated> CreateAndConfigureOpenProjectProject(CreateProjectCommand projectDto, IEnumerable<User> users, User owner)
	{
		OPProjectCreation openProjectProjectCreation = new()
		{
			Name = projectDto.Name,
			Description = new()
			{
				Raw = projectDto.Description ?? string.Empty,
				Format = "markdown"
			},
			Identifier = projectDto.Name.ToLowerInvariant().Replace(" ", "_"),
			Public = true,
			Active = true,
		};
		OPProjectCreated opProjectCreated = await openProjectClient.Projects.CreateAsync(openProjectProjectCreation);
		MembershipCreation ownerMembership = new()
		{
			Links = MembershipCreationLinksBuilder.Build(int.Parse(owner.OpenProjectId), [8], opProjectCreated.Id)
		};
		await openProjectClient.Memberships.CreateAsync(ownerMembership);
		foreach (User projectUser in users.Where(pu => pu.Id != owner.Id))
		{
			int[] roles = [6]; // Owner role is 8, Developer role is 6
			MembershipCreation membershipCreation = new()
			{
				Links = MembershipCreationLinksBuilder.Build(int.Parse(projectUser.OpenProjectId), roles, opProjectCreated.Id)
			};
			await openProjectClient.Memberships.CreateAsync(membershipCreation);
		}
		return opProjectCreated;
	}

	private async Task CancelCreation(long? gitlabId, string? sonarQubeKey, int? openProjectId)
	{
		List<Task<bool>> tasks = new()
		{
			TryDeleteGitlabProject(gitlabId),
			TryDeleteSonarQubeProject(sonarQubeKey),
			TryDeleteOpenprojectProject(openProjectId)
		};

		await Task.WhenAll(tasks);
	}

	private async Task<bool> TryDeleteGitlabProject(long? projectId)
	{
		if (projectId is null) return false;

		try
		{
			ProjectId gitlabId = projectId.Value;
			await gitLabClient.Projects.DeleteAsync(gitlabId);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private async Task<bool> TryDeleteSonarQubeProject(string? projectKey)
	{
		if (string.IsNullOrEmpty(projectKey)) return false;
		try
		{
			await sonarQubeClient.Projects.DeleteAsync(projectKey);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private async Task<bool> TryDeleteOpenprojectProject(int? projectId)
	{
		if (projectId is null) return false;
		try
		{
			await openProjectClient.Projects.DeleteAsync(projectId.Value);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
