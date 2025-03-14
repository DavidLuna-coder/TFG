﻿namespace TFG.Application.Services.Dtos
{
	public record SonarQubeCreateProjectRequestDto
	{
		public string ProjectKey { get; set; } // Key of the project to create
		public string ProjectName { get; set; } // Name of the project to create
		public string DevOpsPlatformSettingId { get; set; } // Identifier of DevOps platform configuration to use.
		public string RepositoryIdentifier { get; set; } // Identifier of the DevOps platform repository to import: - repository slug for GitHub and Bitbucket (Cloud and Server) - repository id for GitLab - repository name for Azure DevOps
		public bool Monorepo { get; set; } // True if project is part of a mono repo.
		public string? ProjectIdentifier { get; set; } // Identifier of the DevOps platform project in which the repository is located. This is only needed for Azure and BitBucket Server platforms
		public string? NewCodeDefinitionType { get; set; } // Project New Code Definition Type New code definitions of the following types are allowed: - PREVIOUS_VERSION - NUMBER_OF_DAYS - REFERENCE_BRANCH - will default to the main branch.
		public string? NewCodeDefinitionValue { get; set; } // Project New Code Definition Value For each new code definition type, a different value is expected: - no value, when the new code definition type is PREVIOUS_VERSION and REFERENCE_BRANCH - a number between 1 and 90, when the new code definition type is NUMBER_OF_DAYS
	}

	public record SonarQubeCreateUserPermissionDto
	{
		public required string Login { get; set; } // User login, username
		public string Permission { get; set; }
		public string ProjectId { get; set; }
		public string ProjectKey { get; set; }
	}

	public static class SonarQubeGeneralPermissionConstants
	{
		public const string Admin = "admin";
		public const string GateAdmin = "gateadmin";
		public const string ProfileAdmin = "profileadmin";
		public const string Provisioning = "provisioning";
		public const string Scan = "scan";
		public const string ApplicationCreator = "applicationcreator";
		public const string PortfolioCreator = "portfolioCreator";
	}

	public static class SonarQubeProjectPermissionConstants
	{
		public const string Admin = "admin";
		public const string CodeViewer = "codeviewer";
		public const string IssueAdmin = "issueadmin";
		public const string SecurityHotspotAdmin = "securityhotspotadmin";
		public const string User = "user";
		public const string Scan = "scan";
	}
}
