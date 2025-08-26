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
using TFG.OpenProjectClient;
using TFG.OpenProjectClient.Models.BasicObjects;
using TFG.OpenProjectClient.Models.Memberships;
using TFG.OpenProjectClient.Models.Projects;
using TFG.SonarQubeClient;
using TFG.SonarQubeClient.Models;
using Project = TFG.Domain.Entities.Project;
using User = TFG.Domain.Entities.User;

namespace TFG.Application.Services.Projects.Commands.UpdateProject;

public class UpdateProjectCommand : IRequest<ProjectDto>
{
	public Guid ProjectId { get; set; }
	public string Name { get; set; }
	public string? Description { get; set; }
	public List<string>? UsersIds { get; set; }
}

public class UpdateProjectCommandHandler(ApplicationDbContext context,
										 IOpenProjectClient openProjectClient,
										 IGitLabClient gitLabClient,
										 ISonarQubeClient sonarQubeClient,
										 IUserInfoAccessor userInfoAccessor,
										 ILogger<UpdateProjectCommandHandler> logger) : IRequestHandler<UpdateProjectCommand, ProjectDto>
{
	public async Task<ProjectDto> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
	{
		var existingProject = await context.Projects
			.Include(p => p.Users)
			.FirstOrDefaultAsync(p => p.Id == request.ProjectId, cancellationToken: cancellationToken) ?? throw new NotFoundException($"El proyecto con id {request.ProjectId} no existe");

		string owner = existingProject.OwnerId;
		var requestUserIds = request.UsersIds ?? new List<string>();

		var projectUserIds = existingProject.Users.Select(u => u.Id).ToList();

		var usersToAddIds = requestUserIds.Except(projectUserIds).ToList();
		var usersToRemoveIds = projectUserIds.Except(requestUserIds).ToList();

		var allUserIds = usersToAddIds.Concat(usersToRemoveIds).ToList();
		var allUsers = await context.Users.Where(u => allUserIds.Contains(u.Id)).ToListAsync();
		var usersToAdd = allUsers.Where(u => usersToAddIds.Contains(u.Id)).ToList();
		var usersToRemove = allUsers.Where(u => usersToRemoveIds.Contains(u.Id)).ToList();

		// Add users
		foreach (var user in usersToAdd)
		{
			existingProject.Users.Add(user);
		}

		// Remove users
		foreach (var user in usersToRemove.Where(u => existingProject.Users.Contains(u)))
		{
			existingProject.Users.Remove(user);
		}

		existingProject.Name = request.Name;
		existingProject.Description = request.Description ?? string.Empty;

		await UpdateProjectInGitlab(request, existingProject, usersToAdd, usersToRemove, owner);
		await UpdateProjectOpenProject(request, existingProject, usersToAdd, usersToRemove, owner);
		await UpdateSonarqubeProject(request, existingProject, usersToAdd, usersToRemove, owner);
		await context.SaveChangesAsync(cancellationToken);

		return existingProject.ToProjectDto();
	}

	private async Task UpdateProjectInGitlab(UpdateProjectCommand request, Project existingProject, List<User> usersToAdd, List<User> usersToRemove, string owner)
	{
		NGitLab.Models.ProjectUpdate projectUpdate = request.ToGitlabProjectUpdate();
		await gitLabClient.Projects.UpdateAsync(existingProject.GitlabId, projectUpdate);

		foreach (var user in usersToRemove.Where(u => u.Id != owner))
		{
			try
			{
				await gitLabClient.Members.RemoveMemberFromProjectAsync(existingProject.GitlabId, long.Parse(user.GitlabId));
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex,
								  "No se pudo eliminar al usuario {UserName} del proyecto {ProjectName} en GitLab. Puede que no exista o que ya haya sido eliminado.",
								  user.UserName,
								  existingProject.Name);
				continue; // Continue even if we can't remove a user
			}
		}

		foreach (var user in usersToAdd)
		{
			ProjectMemberCreate member = new()
			{
				UserId = user.GitlabId,
				AccessLevel = AccessLevel.Developer
			};
			try
			{
				await gitLabClient.Members.AddMemberToProjectAsync(existingProject.GitlabId, member);
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex,
				  "No se pudo Agregar al usuario {UserName} del proyecto {ProjectName} en GitLab. Puede que no exista o que ya haya sido eliminado.",
				  user.UserName,
				  existingProject.Name);
				continue; // Continue even if we can't add a user
			}
		}
	}

	private async Task UpdateProjectOpenProject(UpdateProjectCommand request, Project existingProject, List<User> usersToAdd, List<User> usersToRemove, string owner)
	{
		GetMembershipsQuery query = new();
		foreach (var user in usersToRemove.Where(u => u.Id != owner))
		{
			query.Filters = [
					new OpenProjectFilters()
					{
						Name = "project",
						Operator = "=",
						Values = [existingProject.OpenProjectId.ToString()]
					},
					new OpenProjectFilters()
					{
						Name = "principal",
						Operator = "=",
						Values = [user.OpenProjectId.ToString()]
					}
				];
			var membershipCollection = await openProjectClient.Memberships.GetAsync(query);
			var membershipToRemove = membershipCollection.Embedded.Elements.FirstOrDefault();
			if (membershipToRemove != null)
			{
				try
				{
					await openProjectClient.Memberships.DeleteAsync(membershipToRemove.Id);
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex,
					  "No se pudo eliminar al usuario {UserName} del proyecto {ProjectName} en OpenProject. Puede que no exista o que ya haya sido eliminado.",
					  user.UserName,
					  existingProject.Name);
					continue; // Continue even if we can't remove a user
				}
			}
		}

		foreach (var user in usersToAdd)
		{
			MembershipCreation membershipCreation = new()
			{
				Links = MembershipCreationLinksBuilder.Build(int.Parse(user.OpenProjectId), [6], existingProject.OpenProjectId)
			};
			try
			{
				await openProjectClient.Memberships.CreateAsync(membershipCreation);
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex,
				  "No se pudo agregar al usuario {UserName} al proyecto {ProjectName} en OpenProject. Puede que no exista o que ya haya sido eliminado.",
				  user.UserName,
				  existingProject.Name);
				continue; // Continue even if we can't add a user
			}
		}


		OpenProjectClient.Models.Projects.ProjectUpdate update = new()
		{
			Id = existingProject.OpenProjectId,
			Name = request.Name,
			Identifier = request.Name.ToLowerInvariant().Replace(" ", "_"),
			Description = new()
			{
				Raw = request.Description ?? string.Empty,
				Format = "markdown"
			}
		};

		await openProjectClient.Projects.UpdateAsync(update);
	}

	private async Task UpdateSonarqubeProject(UpdateProjectCommand request, Project existingProject, List<User> usersToAdd, List<User> usersToRemove, string owner)
	{
		foreach (var user in usersToRemove.Where(u => u.Id != owner))
		{
			UserPermission userPermission = new() { Login = user.UserName, Permission = PermissionType.Admin, ProjectKey = existingProject.SonarQubeProjectKey };

			try
			{
				await sonarQubeClient.Permissions.DeleteUserAsync(userPermission);

			}
			catch (Exception ex)
			{
				logger.LogWarning(ex,
				  "No se pudo agregar al usuario {UserName} al proyecto {ProjectName} en Sonarqube. Puede que no exista o que ya haya sido eliminado.",
				  user.UserName,
				  existingProject.Name);
				continue; // Continue even if we can't add a user
			}
		}

		foreach (var user in usersToAdd)
		{
			UserPermission userPermission = new() { Login = user.UserName, Permission = PermissionType.Admin, ProjectKey = existingProject.SonarQubeProjectKey };

			try
			{
				await sonarQubeClient.Permissions.AddUserAsync(userPermission);
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex,
				  "No se pudo agregar al usuario {UserName} al proyecto {ProjectName} en SonarQube. Puede que no exista o que ya haya sido eliminado.",
				  user.UserName,
				  existingProject.Name);
				continue; // Continue even if we can't add a user
			}
		}
		UpdateProjectKey updateProjectKey = new() { From = existingProject.SonarQubeProjectKey, To = request.Name.ToLowerInvariant().Replace(" ", "-") };
		try
		{
			existingProject.SonarQubeProjectKey = updateProjectKey.To;
			await sonarQubeClient.Projects.UpdateKeyAsync(updateProjectKey);
		}
		catch { }
	}
}
