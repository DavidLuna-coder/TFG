﻿using Microsoft.Extensions.DependencyInjection;
using TFG.OpenProjectClient.Impl;

namespace TFG.OpenProjectClient
{
	public static class OpenProjectServiceRegistration
	{
		public static void AddOpenProjectApiClient(this IServiceCollection services, string url, string token)
		{
			services.AddSingleton<IOpenProjectHttpClient, OpenProjectHttpClient>(s => new OpenProjectHttpClient(url, token));
			services.AddSingleton<IProjectsClient, ProjectClient>();
			services.AddSingleton<IUsersClient, UserClient>();
			services.AddSingleton<IWorkPackagesClient, WorkPackagesClient>();
			services.AddSingleton<IMembershipsClient, MembershipClient>();
			services.AddSingleton<IStatusesClient, StatusesClient>();
			services.AddSingleton<IOpenProjectClient, OpenProjectClientImpl>();
		}
	}
}
