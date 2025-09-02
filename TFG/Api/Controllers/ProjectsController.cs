using LinqKit;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Packaging;
using Shared.DTOs.Filters;
using Shared.DTOs.Pagination;
using Shared.DTOs.Projects;
using Shared.DTOs.Projects.Metrics;
using Shared.DTOs.Users;
using TFG.Api.Exeptions;
using TFG.Api.FilterHandlers;
using TFG.Api.Mappers;
using TFG.Application.Interfaces.Projects;
using TFG.Application.Services.Projects.Commands.CreateProject;
using TFG.Application.Services.Projects.Commands.DeleteProject;
using TFG.Application.Services.Projects.Commands.ArchiveProject;
using TFG.Application.Services.Projects.Queries.GetGitlabMetrics;
using TFG.Application.Services.Projects.Queries.GetMosAffectedFiles;
using TFG.Application.Services.Projects.Queries.GetOpenProjectMetrics;
using TFG.Application.Services.Projects.Queries.GetProjectsKpi;
using TFG.Application.Services.Projects.Queries.GetTasksSummary;
using TFG.Domain.Entities;
using TFG.Infrastructure.Data;
using TFG.Application.Security;
using TFG.Application.Services.Projects.Commands.UpdateProject;
using FluentValidation;
using Front.Validators;

namespace TFG.Api.Controllers
{
	[Route("api/projects")]
	[ApiController]
	public class ProjectsController(ApplicationDbContext context, UserManager<User> userManager, IProjectService projectService, IMediator mediator, IUserInfoAccessor userInfoAccessor) : ControllerBase
	{
		private readonly ApplicationDbContext _context = context;
		private readonly UserManager<User> _userManager = userManager;
		private readonly IMediator _mediator = mediator;
		private readonly IUserInfoAccessor _userInfoAccessor = userInfoAccessor;

		private readonly CreateProjectValidator _validator = new();
		private readonly UpdateProjectValidator _updateValidator = new();

		// POST: api/Projects/search
		[HttpPost("search")]
		public async Task<ActionResult<PaginatedResponseDto<FilteredProjectDto>>> SearchProjects([FromBody] FilteredPaginatedRequestDto<ProjectQueryParameters> request)
		{
			var userId = _userInfoAccessor.UserInfo?.UserId;
			var query = new TFG.Application.Services.Projects.Queries.SearchProjects.SearchProjectsQuery
			{
				Request = request,
				UserId = userId
			};
			var result = await _mediator.Send(query);
			return result;
		}

		// GET: api/Projects/5
		[HttpGet("{id}")]
		public async Task<ActionResult<ProjectDto>> GetProject(Guid id)
		{
			var userId = _userInfoAccessor.UserInfo?.UserId;
			var query = new TFG.Application.Services.Projects.Queries.GetProject.GetProjectQuery
			{
				ProjectId = id,
				UserId = userId
			};

			var projectDto = await _mediator.Send(query);
			return projectDto;
		}

		// PUT: api/Projects/5
		// To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
		[HttpPut("{id}")]
		public async Task<IActionResult> PutProject(Guid id, UpdateProjectDto project)
		{
			var validationResult = await _updateValidator.ValidateAsync(project);
			if (!validationResult.IsValid)
			{
				return BadRequest(validationResult.Errors.Select(e => e.ErrorMessage));
			}

			UpdateProjectCommand updateProjectCommand = new()
			{
				ProjectId = id,
				Name = project.Name,
				Description = project.Description,
				UsersIds = project.UsersIds
			};

			var response = await _mediator.Send(updateProjectCommand);

			return Ok(response);
		}

		// POST: api/Projects
		// To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
		[HttpPost]
		public async Task<ActionResult<Project>> CreateProject(CreateProjectDto projectDto)
		{
			var validationResult = await _validator.ValidateAsync(projectDto);
			if (!validationResult.IsValid)
			{
				return BadRequest(validationResult.Errors.Select(e => e.ErrorMessage));
			}

			CreateProjectCommand command = new()
			{
				Description = projectDto.Description,
				Name = projectDto.Name,
				Template = projectDto.Template,
				UsersIds = projectDto.UsersIds ?? new List<string>()
			};

			ProjectDto response = await _mediator.Send(command);
			return CreatedAtAction(nameof(GetProject), new { id = response.Id }, response);
		}

		// DELETE: api/Projects/5
		[HttpDelete("{id}")]
		public async Task<IActionResult> DeleteProject(Guid id)
		{
			DeleteProjectCommand command = new()
			{
				ProjectId = id
			};

			await _mediator.Send(command);
			return NoContent();
		}

		[HttpPost("{id}/users/search")]
		public async Task<IActionResult> GetProjectUsers(Guid id, FilteredPaginatedRequestDto<GetUsersQueryParameters> request)
		{
			var usersQuery = _userManager.Users.AsQueryable();
			IFiltersHandler<User, GetUsersQueryParameters> filtersHandler = new UserFiltersHandler();

			var predicate = filtersHandler.GetFilters(request.Filters);
			//Only users from this project
			predicate = predicate.And(u => u.Projects.Any(p => p.Id == id));
			usersQuery = usersQuery.Where(predicate);


			if (request.PageSize >= 0)
			{
				usersQuery = usersQuery.Skip((request.Page) * request.PageSize).Take(request.PageSize);
			}

			List<User> users = await usersQuery.ToListAsync();
			List<FilteredUserDto> usersDto = users.Select(u => u.ToFilteredUserDto()).ToList();
			PaginatedResponseDto<FilteredUserDto> response = new()
			{
				Items = usersDto,
				TotalCount = usersQuery.Count(),
				PageNumber = request.Page,
				PageSize = request.PageSize
			};
			return Ok(response);
		}

		[HttpPost("{id}/task-summary/search")]
		public async Task<IActionResult> GetTaskSummary(Guid id, [FromBody] FilteredPaginatedRequestDto<GetTaskSummaryQueryFilters> request)
		{
			GetTasksSummaryQuery query = new()
			{
				ProjectId = id,
				Request = new FilteredPaginatedRequestDto<GetTaskSummaryQueryFilters>
				{
					Page = request.Page,
					PageSize = request.PageSize,
					Filters = request.Filters
				}
			};
			try
			{
				var response = await _mediator.Send(query);
				return Ok(response);
			}
			catch (NotFoundException)
			{
				return NotFound();
			}
		}

		[HttpGet("{projectId}/metrics")]
		public async Task<IActionResult> GetMetrics(Guid projectId)
		{
			GetProjectKpisQuery query = new()
			{
				ProjectId = projectId,
			};
			var metrics = await _mediator.Send(query);
			return Ok(metrics);
		}

		[HttpGet("{projectId}/metrics/most-affected-files")]
		public async Task<IActionResult> GetMostAffectedFiles(Guid projectId)
		{
			GetMostAffectedFilesQuery query = new()
			{
				ProjectId = projectId,
			};
			var metrics = await _mediator.Send(query);
			return Ok(metrics);
		}

		[HttpPost("{projectId}/gitlab-metrics")]
		public async Task<IActionResult> GetGitlabMetrics(Guid projectId, [FromBody] GetGitlabMetricsDto? request)
		{
			GetGitlabMetricsQuery query = new()
			{
				UserId = request?.UserId,
				ProjectId = projectId
			};

			var result = await _mediator.Send(query);

			return Ok(result);
		}

		[HttpPost("{projectId}/openproject-metrics")]
		public async Task<IActionResult> GetOpenProjectMetrics(Guid projectId, [FromBody] GetOpenProjectMetricsDto? request)
		{
			GetOpenProjectMetricsQuery query = new()
			{
				UserId = request?.UserId,
				ProjectId = projectId
			};

			var result = await _mediator.Send(query);

			return Ok(result);
		}

		[HttpPost("{id}/archive")]
		public async Task<IActionResult> ArchiveProject(Guid id)
		{
			var project = await _context.Projects.FindAsync(id);
			if (project == null)
				return NotFound();

			ArchiveProjectCommandPermission archiveProjectCommandPermission = new(_userInfoAccessor, _context);
			var command = new ArchiveProjectCommand { ProjectId = id, Archive = true };
			bool isAdmin = _userInfoAccessor.UserInfo?.IsAdmin is not null && _userInfoAccessor.UserInfo.IsAdmin;
			if (!isAdmin && !await archiveProjectCommandPermission.HasPermissionAsync(command))
				throw new ForbiddenException("No tienes permisos para archivar proyectos.");

			project.IsArchived = true;
			_context.Projects.Update(project);
			await _context.SaveChangesAsync();
			return NoContent();
		}

		[HttpPost("{id}/unarchive")]
		public async Task<IActionResult> UnarchiveProject(Guid id)
		{
			var project = await _context.Projects.FindAsync(id);
			if (project == null)
				return NotFound();

			ArchiveProjectCommandPermission archiveProjectCommandPermission = new(_userInfoAccessor, _context);
			var command = new ArchiveProjectCommand { ProjectId = id, Archive = false };
			bool isAdmin = _userInfoAccessor.UserInfo?.IsAdmin is not null && _userInfoAccessor.UserInfo.IsAdmin;
			if (!isAdmin && !await archiveProjectCommandPermission.HasPermissionAsync(command))
				throw new ForbiddenException("No tienes permisos para desarchivar proyectos.");

			project.IsArchived = false;
			_context.Projects.Update(project);
			await _context.SaveChangesAsync();
			return NoContent();
		}
	}
}
