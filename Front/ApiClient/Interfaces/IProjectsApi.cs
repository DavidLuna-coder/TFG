﻿using Shared.DTOs.Pagination;
using Shared.DTOs.Projects;

namespace Front.ApiClient.Interfaces
{
    public interface IProjectsApi
    {
        public Task<ProjectDto> CreateProject(CreateProjectDto createProjectDto);
        public Task<PaginatedResponseDto<FilteredProjectDto>> GetProjects(PaginatedRequestDto<ProjectQueryParameters> request);
        public Task<ProjectDto> GetProject(Guid projectId);

	}
}
