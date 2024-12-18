﻿namespace Shared.DTOs.Projects
{
	public record ProjectDto
	{
		public Guid Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;
		public List<string> UsersIds { get; set; } = [];
		public DateTime CreatedAt { get; set; }
	}
}
