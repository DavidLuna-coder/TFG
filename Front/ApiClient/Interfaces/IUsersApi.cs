﻿using Shared.DTOs.Pagination;
using Shared.DTOs.Users;

namespace Front.ApiClient.Interfaces
{
	public interface IUsersApi
	{
		Task<PaginatedResponseDto<FilteredUserDto>> GetFilteredUsers(FilteredPaginatedRequestDto<GetUsersQueryParameters> queryParameters, CancellationToken cancellationToken = default);
    }
}
