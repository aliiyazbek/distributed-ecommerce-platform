using FluentValidation;
using Modules.Orders.Contracts;

namespace Modules.Orders.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.Items)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Order must contain at least one item.")
            .Must(items => items.All(i => i != null)).WithMessage("Order contains an invalid item.")
            .Must(items => items.Select(i => i.ProductId).Distinct().Count() == items.Count)
            .WithMessage("Each product can only appear once in an order.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty().WithMessage("Product id is required.");
            item.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("Quantity must be at least 1.");
        });
    }
}
