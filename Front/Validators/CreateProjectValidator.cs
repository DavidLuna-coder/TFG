using FluentValidation;
using Shared.DTOs;
using Shared.DTOs.Projects;

namespace Front.Validators
{
	public class CreateProjectValidator : AbstractValidator<CreateProjectDto>
	{
		public CreateProjectValidator() 
		{
			RuleFor(x => x.Name)
				.NotNull()
				.NotEmpty().WithMessage("El nombre del proyecto es obligatorio")
				.Matches("^[a-zA-Z0-9 ]*$").WithMessage("El nombre del proyecto no puede contener símbolos ni tildes");
		}
		public Func<object, string, Task<IEnumerable<string>>> ValidateValues => async (model, propertyName) =>
		{
			var result = await ValidateAsync(ValidationContext<CreateProjectDto>.CreateWithOptions((CreateProjectDto)model, x => x.IncludeProperties(propertyName)));
			if (result.IsValid)
				return Array.Empty<string>();
			return result.Errors.Select(e => e.ErrorMessage);
		};
	}
}
