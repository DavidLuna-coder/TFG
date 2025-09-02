using FluentValidation;
using Shared.DTOs.Projects;

namespace Front.Validators
{
    public class UpdateProjectValidator : AbstractValidator<UpdateProjectDto>
    {
        public UpdateProjectValidator()
        {
            RuleFor(x => x.Name)
                .NotNull()
                .NotEmpty().WithMessage("El nombre del proyecto es obligatorio")
                .Matches("^[a-zA-Z0-9 ]*$").WithMessage("El nombre del proyecto no puede contener símbolos ni tildes");
        }

        public Func<object, string, Task<IEnumerable<string>>> ValidateValues => async (model, propertyName) =>
        {
            var result = await ValidateAsync(ValidationContext<UpdateProjectDto>.CreateWithOptions((UpdateProjectDto)model, x => x.IncludeProperties(propertyName)));
            if (result.IsValid)
                return Array.Empty<string>();
            return result.Errors.Select(e => e.ErrorMessage);
        };
    }
}