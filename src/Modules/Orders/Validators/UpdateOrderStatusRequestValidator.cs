using FluentValidation;
using Modules.Orders.Contracts;
using Modules.Orders.Entities;

namespace Modules.Orders.Validators;

public class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Status is required.")
            .IsEnumName(typeof(OrderStatus), caseSensitive: false)
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
    }
}
