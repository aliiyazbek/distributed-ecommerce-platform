using FluentValidation;
using Modules.Products.Contracts;

namespace Modules.Products.Validators;

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Product name is required.");
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Price must be greater than zero.");
        RuleFor(x => x.Category).NotEmpty().WithMessage("Category is required.");
        RuleFor(x => x.AvailableQuantity).GreaterThanOrEqualTo(0).WithMessage("Quantity cannot be negative.");
        RuleFor(x => x.Image).NotNull().WithMessage("A product image is required.");
    }
}