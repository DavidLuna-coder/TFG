﻿using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NGitLab;
using NGitLab.Models;
using Shared.DTOs.Projects;
using TFG.Api.Mappers;
using TFG.Application.Dtos;
using TFG.Application.Interfaces.Projects;
using TFG.Domain.Results;
using TFG.Infrastructure.Data;
using Project = TFG.Domain.Entities.Project;
using GitlabProject = NGitLab.Models.Project;
using User = TFG.Domain.Entities.User;
using OPProjectCreation = TFG.OpenProjectClient.Models.Projects.ProjectCreation;
using OPProjectCreated = TFG.OpenProjectClient.Models.Projects.ProjectCreated;
using TFG.SonarQubeClient;
using TFG.SonarQubeClient.Models;
using TFG.OpenProjectClient;
using TFG.OpenProjectClient.Models.Memberships;
using TFG.OpenProjectClient.Models.Statuses;
using TFG.OpenProjectClient.Models.WorkPackages;
using TFG.OpenProjectClient.Models.BasicObjects;

namespace TFG.Application.Services.Projects
{
	public class ProjectService(ApplicationDbContext dbContext, UserManager<User> userManager, IHttpContextAccessor httpContextAccessor, IGitLabClient gitLabClient, ISonarQubeClient sonarClient, IOpenProjectClient openProjectClient, ILogger<ProjectService> logger) : IProjectService
	{
		private readonly ApplicationDbContext _dbContext = dbContext;
		private readonly UserManager<User> _userManager = userManager;
		private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
		private readonly IGitLabClient _gitLabClient = gitLabClient;
		private readonly ISonarQubeClient _sonarQubeClient = sonarClient;
		private readonly IOpenProjectClient _openProjectClient = openProjectClient;

		public async Task<Result<Project>> CreateProject(CreateProjectDto projectDto)
		{
			//Get the user id from the token
			UserInfo userInfo = _httpContextAccessor.HttpContext!.Items["UserInfo"] as UserInfo ?? new();
			projectDto.UsersIds ??= [];

			var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userInfo.UserId);
			if (user == null) return new Result<Project>(["User not found"]);


			IEnumerable<User> projectUsers = await _userManager.Users.Where(u => projectDto.UsersIds.Any(id => id == u.Id)).ToListAsync();
			var gitlabProjectResult = await CreateAndConfigureGitlabProject(projectDto, user, projectUsers);
			if (!gitlabProjectResult.Success) return new Result<Project>(gitlabProjectResult.Errors);

			//Create Project in SonarQube
			string sonarQubeProjectKey = projectDto.Name.ToLowerInvariant().Replace(" ", "_");
			string sonarQubeRepositoryIdentifier = gitlabProjectResult.Value.Id.ToString();
			await CreateAndConfigureSonarQubeProject(projectDto, sonarQubeProjectKey, sonarQubeRepositoryIdentifier, projectUsers);

			OPProjectCreated opProjectCreated =  await	CreateAndConfigureOpenProjectProject(projectDto, projectUsers);

			return await CreateProjectInDatabase(projectDto, projectUsers, gitlabProjectResult, sonarQubeProjectKey, opProjectCreated.Id);
		}

		private async Task<Result<Project>> CreateProjectInDatabase(CreateProjectDto projectDto, IEnumerable<User> projectUsers, Result<GitlabProject> gitlabProjectResult, string sonarQubeProjectKey, int openProjectProjectId)
		{
			//Create the project in the database
			Project newProject = projectDto.ToProject();
			newProject.SonarQubeProjectKey = sonarQubeProjectKey;
			newProject.GitlabId = gitlabProjectResult.Value.Id.ToString();
			newProject.OpenProjectId = openProjectProjectId;
			newProject.Users = projectUsers.ToList();
			newProject.CreatedAt = DateTime.UtcNow;
			_dbContext.Projects.Add(newProject);
			await _dbContext.SaveChangesAsync();
			return new Result<Project>(newProject);
		}

		public async Task<Result<bool>> DeleteProject(Guid id)
		{
			UserInfo userInfo = _httpContextAccessor.HttpContext!.Items["UserInfo"] as UserInfo ?? new();
			if (!userInfo.IsAdmin) return new Result<bool>(["Not enough permissions to delete de project"]);

			Project? projectToDelete = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == id);
			if (projectToDelete is null) return new Result<bool>(["The project does not exist"]);

			//Delete the project in Gitlab
			await _gitLabClient.Projects.DeleteAsync(projectToDelete.GitlabId);

			//Delete the project in OpenProject
			await _openProjectClient.Projects.DeleteAsync(projectToDelete.OpenProjectId);

			await _sonarQubeClient.Projects.DeleteAsync(projectToDelete.SonarQubeProjectKey);
			//Delete the project in the database
			_dbContext.Projects.Remove(projectToDelete);
			await _dbContext.SaveChangesAsync();
			return true;
		}

		public async Task<List<ProjectTaskDto>> GetProjectTasks(Guid projectId, ProjectTaskQueryParameters requestDto)
		{
			int openProjectProjectId = await _dbContext.Projects.Where(p => p.Id == projectId).Select(p => p.OpenProjectId).FirstOrDefaultAsync();
			if (openProjectProjectId == default)
			{
				throw new ArgumentException("The project does not exist");
			}

			var statuses = await _openProjectClient.Statuses.GetStatusesAsync();

			IEnumerable<Status> closedStatuses = statuses.Embedded.Elements.Where(s => s.IsClosed);
			IEnumerable<Status> openStatuses = statuses.Embedded.Elements.Where(s => !s.IsClosed);
			OpenProjectClient.Models.OpenProjectCollection<WorkPackage> closedTasks = new();
			if (closedStatuses.Any())
			{
				GetWorkPackagesQuery closedQuery = new()
				{
					Filters = [new OpenProjectFilters() { Name = "status", Operator = "=", Values = [.. closedStatuses.Select(s => s.Id.ToString())] }],
				};
				closedTasks = await _openProjectClient.WorkPackages.GetAsync(openProjectProjectId, closedQuery);
			}
			GetWorkPackagesQuery query = new()
			{
				Filters = [new OpenProjectFilters() { Name = "status", Operator = "=", Values = [.. openStatuses.Select(s => s.Id.ToString())] }],
			};
			OpenProjectClient.Models.OpenProjectCollection<WorkPackage> openTasks = await _openProjectClient.WorkPackages.GetAsync(openProjectProjectId, query);

			var result = new List<ProjectTaskDto>();
			result.AddRange(closedTasks.Embedded.Elements.Select(t => t.ToProjectTaskDto(true)));
			result.AddRange(openTasks.Embedded.Elements.Select(t => t.ToProjectTaskDto(false)));
			return result.ToList();
		}

		private async Task<OPProjectCreated> CreateAndConfigureOpenProjectProject(CreateProjectDto projectDto, IEnumerable<User> users)
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

			OPProjectCreated opProjectCreated = await _openProjectClient.Projects.CreateAsync(openProjectProjectCreation);

			foreach (int userOpenProjectId in users.Select(u => int.Parse(u.OpenProjectId)))
			{
				MembershipCreation membershipCreation = new()
				{
					Links = MembershipCreationLinksBuilder.Build(userOpenProjectId, [6], opProjectCreated.Id)
				};
				await _openProjectClient.Memberships.CreateAsync(membershipCreation);
			}

			return opProjectCreated;
		}

		private async Task<BoundedProject> CreateAndConfigureSonarQubeProject(CreateProjectDto projectDto, string projectKey, string repositoryIdentifier, IEnumerable<User> usersToInclude)
		{
			var dopSettings = await _sonarQubeClient.DopTranslations.GetDopSettingsAsync();

			string gilabId = dopSettings.DopSettings.First(ds => ds.Type == "gitlab").Id;
			ProjectBinding projectBinding = new() { DevOpsPlatformSettingId = gilabId, ProjectKey = projectKey, ProjectName = projectDto.Name, RepositoryIdentifier = repositoryIdentifier, Monorepo = true };
			BoundedProject project = await _sonarQubeClient.DopTranslations.BoundProjectAsync(projectBinding);

			foreach (var user in usersToInclude)
			{
				UserPermission userPermission = new() { Login = user.UserName!, ProjectKey = projectKey, Permission = PermissionType.Admin };
				await _sonarQubeClient.Permissions.AddUserAsync(userPermission);
			}
			return project;
		}

		private async Task<Result<GitlabProject>> CreateAndConfigureGitlabProject(CreateProjectDto projectDto, User user, IEnumerable<User> projectUsers)
		{
			//Create the project in Gitlab
			ProjectCreate gitLabProject = projectDto.ToGitlabProjectCreate();
			GitlabProject createdProject = await _gitLabClient.Projects.CreateAsync(gitLabProject);

			foreach (User projectUser in projectUsers)
			{
				ProjectMemberCreate member = new()
				{
					UserId = projectUser.GitlabId,
					AccessLevel = projectUser.Id != user.Id ? AccessLevel.Developer : AccessLevel.Owner
				};
				await _gitLabClient.Members.AddMemberToProjectAsync(createdProject.Id, member);
			}

			return createdProject;
		}
	}

}
