﻿using AutoMapper;
using LinqKit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Shared.DTOs.Pagination;
using Shared.DTOs.Users;
using System.Linq.Expressions;
using TFG.Model.Entities;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace TFG.Api.Controllers
{
	[Route("api/users")]
	[ApiController]
	public class UsersController(UserManager<User> userManager, IMapper mapper) : ControllerBase
	{
		private readonly UserManager<User> _userManager = userManager;
		private readonly IMapper _mapper = mapper;
		// GET: api/<UsersController>
		[HttpPost("search")]
		public IActionResult Get(PaginatedRequestDto<GetUsersQueryParameters> request)
		{
			var usersQuery = _userManager.Users.AsQueryable();

			// Aplicar filtros
			var predicate = PredicateBuilder.New<User>(true);
			if(request.Filters != null)
			{
				predicate = predicate.And(u => request.Filters.Ids == null || request.Filters.Ids.Count == 0 || request.Filters.Ids.Contains(u.Id));
				predicate = predicate.And(u => string.IsNullOrEmpty(request.Filters.FirstName) || u.FirstName.Contains(request.Filters.FirstName));
				predicate = predicate.And(u => string.IsNullOrEmpty(request.Filters.LastName) || u.LastName.Contains(request.Filters.LastName));
			}
			usersQuery = usersQuery.Where(predicate);

			if (request.PageSize >= 0)
			{
				usersQuery = usersQuery.Skip((request.Page) * request.PageSize).Take(request.PageSize);
			}

			List<User> users = [.. usersQuery];
			List<FilteredUserDto> usersDto = _mapper.Map<List<FilteredUserDto>>(users);
			PaginatedResponseDto<FilteredUserDto> response = new()
			{
				Items = usersDto,
				TotalCount = usersQuery.Count(),
				PageNumber = request.Page,
				PageSize = request.PageSize
			};
			return Ok(response);
		}

		// GET api/<UsersController>/5
		[HttpGet("{id}")]
		public IActionResult Get(string id)
		{
			var usersQuery = _userManager.Users.AsQueryable();
			User? user = usersQuery.FirstOrDefault(u => u.Id == id);
			if(user == null) return NotFound();
			
			UserDto userDto = _mapper.Map<UserDto>(user);
			return Ok(userDto);
		}

		// POST api/<UsersController>
		[HttpPost]
		public IActionResult Post([FromBody] string value)
		{
			return Created("", "");
		}

		// PUT api/<UsersController>/5
		[HttpPut("{id}")]
		public void Put(string id, [FromBody] string value)
		{
		}

		// DELETE api/<UsersController>/5
		[HttpDelete("{id}")]
		public void Delete(string id)
		{
		}
	}
}