﻿using LinqKit;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Packaging;
using Shared.DTOs.Filters;
using Shared.DTOs.Pagination;
using Shared.DTOs.Projects;
using Shared.DTOs.Users;
using TFG.Api.Exeptions;
using TFG.Api.FilterHandlers;
using TFG.Api.Mappers;
using TFG.Application.Interfaces.Projects;
using TFG.Application.Services.Projects.Queries.GetMosAffectedFiles;
using TFG.Application.Services.Projects.Queries.GetProjectsKpi;
using TFG.Application.Services.Projects.Queries.GetTasksSummary;
using TFG.Infrastructure.Data;
using TFG.Model.Entities;

namespace TFG.Api.Controllers
{
	[Route("api/projects")]
	[ApiController]
	public class ProjectsController(ApplicationDbContext context, UserManager<User> userManager, IProjectService projectService, IMediator mediator) : ControllerBase
	{
		private readonly ApplicationDbContext _context = context;
		private readonly UserManager<User> _userManager = userManager;
		private readonly IProjectService _projectService = projectService;
		private readonly IMediator _mediator = mediator;

		// POST: api/Projects/search
		[HttpPost("search")]
		public async Task<ActionResult<PaginatedResponseDto<FilteredProjectDto>>> SearchProjects([FromBody] FilteredPaginatedRequestDto<ProjectQueryParameters> request)
		{
			var projectsQuery = _context.Projects.AsQueryable();

			// Aplicar filtros
			IFiltersHandler<Project, ProjectQueryParameters> projectFilterHandler = new ProjectFiltersHandler();
			var predicate = projectFilterHandler.GetFilters(request.Filters);
			projectsQuery = projectsQuery.Where(predicate);

			// Paginación
			var totalItems = await projectsQuery.CountAsync();
			var projects = await projectsQuery
				.Skip((request.Page) * request.PageSize)
				.Take(request.PageSize)
				.ToListAsync();

			List<FilteredProjectDto> items = projects.Select(p => p.ToFilteredProjectDto()).ToList();

			return new PaginatedResponseDto<FilteredProjectDto>
			{
				Items = items,
				TotalCount = totalItems,
				PageNumber = request.Page,
				PageSize = request.PageSize
			};
		}

		// GET: api/Projects/5
		[HttpGet("{id}")]
		public async Task<ActionResult<ProjectDto>> GetProject(Guid id)
		{
			Project? project = await _context.Projects.FindAsync(id);

			if (project == null)
			{
				return NotFound();
			}
			ProjectDto projectDto = project.ToProjectDto();
			return projectDto;
		}

		// PUT: api/Projects/5
		// To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
		[HttpPut("{id}")]
		public async Task<IActionResult> PutProject(Guid id, UpdateProjectDto project)
		{
			var existingProject = await _context.Projects
				.Include(p => p.Users)
				.FirstOrDefaultAsync(p => p.Id == id);

			if (existingProject is null)
			{
				return NotFound();
			}

			project.ToProject(existingProject);

			project.UsersIds ??= [];
			var updatedUsers = await _userManager.Users
				.Where(u => project.UsersIds.Contains(u.Id))
				.ToListAsync();
			existingProject.Users.Clear(); // Remove all users
			existingProject.Users.AddRange(updatedUsers); // Add the updated users
			_context.Projects.Update(existingProject);

			try
			{
				await _context.SaveChangesAsync();
			}
			catch (DbUpdateConcurrencyException)
			{
				if (!ProjectExists(id))
				{
					return NotFound();
				}
				else
				{
					throw;
				}
			}

			var response = existingProject.ToProjectDto();
			return Ok(response);
		}

		// POST: api/Projects
		// To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
		[HttpPost]
		public async Task<ActionResult<Project>> CreateProject(CreateProjectDto projectDto)
		{
			var project = projectDto.ToProject();
			var result = await _projectService.CreateProject(projectDto);

			if (!result.Success)
			{
				return BadRequest(result.Errors);
			}

			ProjectDto projectResponse = result.Value.ToProjectDto();
			return CreatedAtAction(nameof(GetProject), new { id = project.Id }, projectResponse);
		}

		// DELETE: api/Projects/5
		[HttpDelete("{id}")]
		public async Task<IActionResult> DeleteProject(Guid id)
		{
			var result = await _projectService.DeleteProject(id);
			if(!result.Success)
			{
				return BadRequest();
			}
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
		public async Task<IActionResult> GetTaskSummary(Guid id, [FromBody] PaginatedRequestDtoBase request)
		{
			GetTasksSummaryQuery query = new()
			{
				ProjectId = id,
				Request = new FilteredPaginatedRequestDto<GetTaskSummaryQueryFilters>
				{
					Page = request.Page,
					PageSize = request.PageSize
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
		private bool ProjectExists(Guid id)
		{
			return _context.Projects.Any(e => e.Id == id);
		}
	}
}
