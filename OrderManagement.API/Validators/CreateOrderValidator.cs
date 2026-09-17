using FluentValidation;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.Validators
{
    public class CreateOrderValidator : AbstractValidator<CreateOrderRequest>
    {
        public CreateOrderValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.ShippingAddress).NotEmpty().MaximumLength(500);
            RuleFor(x => x.Items).NotEmpty()
                .WithMessage("Order must contain at least one item.");
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId).NotEmpty();
                item.RuleFor(i => i.Quantity).GreaterThan(0);
            });
        }
    }
}
