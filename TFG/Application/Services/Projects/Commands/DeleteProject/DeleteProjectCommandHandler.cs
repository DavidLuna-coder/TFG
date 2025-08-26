using MediatR;
using Microsoft.EntityFrameworkCore;
using NGitLab;
using TFG.Api.Exeptions;
using TFG.Application.Security;
using TFG.Domain.Entities;
using TFG.Infrastructure.Data;
using TFG.OpenProjectClient;
using TFG.SonarQubeClient;

namespace TFG.Application.Services.Projects.Commands.DeleteProject
{
	public class DeleteProjectCommandHandler(ApplicationDbContext dbContext, IUserInfoAccessor userInfo, IGitLabClient gitlabClient, ISonarQubeClient sonarQubeClient, IOpenProjectClient openProjectClient, ILogger<DeleteProjectCommandHandler> logger) : IRequestHandler<DeleteProjectCommand>
	{
		public async Task Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
		{
			Project projectToDelete = await dbContext.Projects.Include(p => p.ProjectExperiences).FirstOrDefaultAsync(p => p.Id == request.ProjectId) ?? throw new NotFoundException($"Project with id {request.ProjectId} does not exist");
			List<Task> tasks = [];

			if (!string.IsNullOrWhiteSpace(projectToDelete.GitlabId))
			{
				tasks.Add(SafeDelete(() => gitlabClient.Projects.DeleteAsync(projectToDelete.GitlabId, cancellationToken)));
			}

			if (projectToDelete.OpenProjectId != 0)
			{
				tasks.Add(SafeDelete(() => openProjectClient.Projects.DeleteAsync(projectToDelete.OpenProjectId)));
			}

			if (!string.IsNullOrWhiteSpace(projectToDelete.SonarQubeProjectKey))
			{
				tasks.Add(SafeDelete(() => sonarQubeClient.Projects.DeleteAsync(projectToDelete.SonarQubeProjectKey)));
			}

			await Task.WhenAll(tasks);
			projectToDelete.ProjectExperiences.Clear();
			dbContext.Projects.Remove(projectToDelete);
			await dbContext.SaveChangesAsync(cancellationToken);

		}

		private async Task SafeDelete(Func<Task> deleteOperation)
		{
			try
			{
				await deleteOperation();
			}
			catch(Exception ex)
			{
				logger.LogError(ex, "Algún fallo ha ocurrido en el borrado al comunicarse con las diferetes apis");
			}
		}
	}
}
