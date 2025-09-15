using MediatR;
using Microsoft.AspNetCore.Identity;
using NGitLab;
using Shared.DTOs.Pagination;
using Shared.DTOs.Projects;
using TFG.Api.Exeptions;
using TFG.Infrastructure.Data;
using TFG.OpenProjectClient;
using TFG.OpenProjectClient.Models;
using TFG.OpenProjectClient.Models.BasicObjects;
using TFG.OpenProjectClient.Models.WorkPackages;
using Microsoft.EntityFrameworkCore;
using User = TFG.Domain.Entities.User;

namespace TFG.Application.Services.Projects.Queries.GetTasksSummary
{
	public class GetTasksSummaryQueryHandler(
	ApplicationDbContext dbContext,
	IOpenProjectClient openProjectClient,
	UserManager<User> userManager,
	IGitLabClient gitlabClient)
	: IRequestHandler<GetTasksSummaryQuery, PaginatedResponseDto<TaskSummaryDto>>
	{
		public async Task<PaginatedResponseDto<TaskSummaryDto>> Handle(GetTasksSummaryQuery request, CancellationToken cancellationToken)
		{
			var project = await dbContext.Projects
				.AsNoTracking()
				.FirstOrDefaultAsync(p => p.Id == request.ProjectId, cancellationToken)
				?? throw new NotFoundException("Project not found");

			var openProjectFilters = await BuildOpenProjectFiltersAsync(request, cancellationToken);

			var query = new GetWorkPackagesQuery
			{
				PageSize = request.Request.PageSize,
				Offset = request.Request.Page * request.Request.PageSize,
				Filters = openProjectFilters
			};

			var workPackagesCollection = await openProjectClient.WorkPackages.GetAsync(project.OpenProjectId, query);

			var tasks = await MapToTaskSummariesAsync(workPackagesCollection, long.Parse(project.GitlabId));

			return new PaginatedResponseDto<TaskSummaryDto>
			{
				Items = tasks,
				PageNumber = request.Request.Page,
				PageSize = request.Request.PageSize,
				TotalCount = workPackagesCollection.Total
			};
		}

		private async Task<OpenProjectFilters[]?> BuildOpenProjectFiltersAsync(GetTasksSummaryQuery query, CancellationToken cancellationToken)
		{
			if (query.Request.Filters?.UserIds is not { Length: > 0 })
				return null;

			var openProjectUserIds = await userManager.Users
				.Where(u => query.Request.Filters.UserIds.Contains(u.Id))
				.Select(u => u.OpenProjectId)
				.ToArrayAsync(cancellationToken);

			return openProjectUserIds.Length > 0
				? [new OpenProjectFilters { Name = "assignee", Operator = "=", Values = openProjectUserIds }]
				: null;
		}

		private async Task<List<TaskSummaryDto>> MapToTaskSummariesAsync(OpenProjectCollection<WorkPackage> collection, long gitlabProjectId)
		{
			var now = DateTime.UtcNow;
			// Obtener todos los statuses una sola vez
			var statusesCollection = await openProjectClient.Statuses.GetStatusesAsync();
			var statuses = statusesCollection.Embedded.Elements.ToDictionary(s => s.Id, s => s);

			var taskSummaries = await Task.WhenAll(collection.Embedded.Elements.Select(async wp =>
			{
				var gitlabMergeRequests = await openProjectClient.WorkPackages.GetGitlabMergeRequestsAsync(wp.Id);

				var mergeRequests = gitlabMergeRequests.Embedded.Elements.Select(mr => new TaskSummaryMergeRequestInfo
				{
					Title = mr.Title,
					Id = mr.Number,
					CommitIds = gitlabClient.GetMergeRequest(gitlabProjectId).Commits(mr.Number).All
						.Select(c => c.ShortId)
						.ToList(),
				}).ToList();

				 // Obtener el statusId del work package
				int? statusId = null;
				if (wp.Links.Status != null && int.TryParse(wp.Links.Status.Href?.Split('/').LastOrDefault(), out var parsedId))
				{
					statusId = parsedId;
				}

				bool isClosed = false;
				if (statusId.HasValue && statuses.TryGetValue(statusId.Value, out var status))
				{
					isClosed = status.IsClosed;
				}

				bool isOverdue = wp.DueDate.HasValue && wp.DueDate.Value < now && !isClosed;

				return new TaskSummaryDto
				{
					Name = wp.Subject,
					OpenProjectTaskId = wp.Id,
					MergeRequests = mergeRequests,
					IsOverdue = isOverdue
				};
			}));

			return taskSummaries.ToList();
		}
	}
}
