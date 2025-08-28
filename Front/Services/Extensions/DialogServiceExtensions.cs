using Front.Components;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Front.Services.Extensions
{
	public static class DialogServiceExtensions
	{
		public static Task<IDialogReference> ShowConfirmationDialog(this IDialogService dialogService, EventCallback OnConfirmed, string? title = null, string? message = null)
		{
			var parameters = new DialogParameters();

			if (!string.IsNullOrEmpty(title))
			{
				parameters.Add(nameof(ConfirmationModal.Title), title);
			}
			if (!string.IsNullOrEmpty(message))
			{
				parameters.Add(nameof(ConfirmationModal.Content), message);
			}
			if (OnConfirmed.HasDelegate)
			{
				parameters.Add(nameof(ConfirmationModal.OnConfirmed), OnConfirmed);
			}

			var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
			return dialogService.ShowAsync<ConfirmationModal>(title, parameters, options);

		}
	}
}
