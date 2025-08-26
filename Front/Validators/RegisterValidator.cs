using FluentValidation;
using Shared.DTOs;

namespace Front.Validators
{
	public class RegisterValidator : AbstractValidator<LoginRequestDto>
	{
		public RegisterValidator() {
			RuleFor(x => x.Email).NotNull().NotEmpty().EmailAddress().WithMessage("La dirección de correo es obligatoria");

			RuleFor(person => person.Password).NotEmpty().WithMessage("La contraseña es obligatoria.");
			RuleFor(person => person.Password).MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.");
			RuleFor(person => person.Password).Matches("[A-Z]").WithMessage("La contraseña debe contener al menos una letra mayúscula.");
			RuleFor(person => person.Password).Matches("[a-z]").WithMessage("La contraseña debe contener al menos una letra minúscula.");
			RuleFor(person => person.Password).Matches("[0-9]").WithMessage("La contraseña debe contener al menos un número.");
			RuleFor(person => person.Password).Matches("[!@#$%^&*]").WithMessage("La contraseña debe contener al menos un símbolo.");
		}

		public Func<object, string, Task<IEnumerable<string>>> ValidateValues => async (model, propertyName) =>
			{
				var result = await ValidateAsync(ValidationContext<LoginRequestDto>.CreateWithOptions((LoginRequestDto)model, x => x.IncludeProperties(propertyName)));
				if (result.IsValid)
					return Array.Empty<string>();
				return result.Errors.Select(e => e.ErrorMessage);
			};

	}

	public class LoginValidator : AbstractValidator<LoginRequestDto>
	{
		public LoginValidator()
		{
			RuleFor(x => x.Email).NotNull().NotEmpty().EmailAddress().WithMessage("La dirección de correo es obligatoria");

			RuleFor(person => person.Password).NotEmpty().WithMessage("La contraseña es obligatoria.");
		}

		public Func<object, string, Task<IEnumerable<string>>> ValidateValues => async (model, propertyName) =>
		{
			var result = await ValidateAsync(ValidationContext<LoginRequestDto>.CreateWithOptions((LoginRequestDto)model, x => x.IncludeProperties(propertyName)));
			if (result.IsValid)
				return Array.Empty<string>();
			return result.Errors.Select(e => e.ErrorMessage);
		};

	}
}
