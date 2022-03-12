using FluentAssertions;
using Xunit;

namespace IntroductionToEventSourcing.GettingStateFromEvents.Solution3;

// EVENTS
public record ShoppingCartOpened(
    Guid ShoppingCartId,
    Guid ClientId
);

public record ProductItemAddedToShoppingCart(
    Guid ShoppingCartId,
    PricedProductItem ProductItem
);

public record ProductItemRemovedFromShoppingCart(
    Guid ShoppingCartId,
    PricedProductItem ProductItem
);

public record ShoppingCartConfirmed(
    Guid ShoppingCartId,
    DateTime ConfirmedAt
);

public record ShoppingCartCancelled(
    Guid ShoppingCartId,
    DateTime CanceledAt
);

// VALUE OBJECTS
public record PricedProductItem(
    ProductItem ProductItem,
    decimal UnitPrice
)
{
    public Guid ProductId => ProductItem.ProductId;
    public int Quantity => ProductItem.Quantity;

    public decimal TotalPrice => Quantity * UnitPrice;
}

public record ProductItem(
    Guid ProductId,
    int Quantity
);

// ENTITY
public record ShoppingCart(
    Guid Id,
    Guid ClientId,
    ShoppingCartStatus Status,
    PricedProductItem[] ProductItems,
    DateTime? ConfirmedAt = null,
    DateTime? CanceledAt = null
)
{
    public static ShoppingCart Default() =>
        new (default, default, default, Array.Empty<PricedProductItem>());
};

public enum ShoppingCartStatus
{
    Pending = 1,
    Confirmed = 2,
    Canceled = 3
}

public class GettingStateFromEventsTests
{
    /// <summary>
    /// Solution 1 - Fully immutable and functional with linq Aggregate method
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    private static ShoppingCart GetShoppingCart(IEnumerable<object> events) =>
        events.Aggregate(ShoppingCart.Default(), When);

    private static ShoppingCart When(ShoppingCart shoppingCart, object @event)
    {
        return @event switch
        {
            ShoppingCartOpened(var shoppingCartId, var clientId) =>
                shoppingCart with
                {
                    Id = shoppingCartId,
                    ClientId = clientId,
                    Status = ShoppingCartStatus.Pending
                },
            ProductItemAddedToShoppingCart(_, (var (productId, _), var unitPrice) pricedProductItem) =>
                shoppingCart with
                {
                    ProductItems = shoppingCart.ProductItems
                        .Union(new [] { pricedProductItem })
                        .GroupBy(pi => pi.ProductId)
                        .Select(group => group.Count() == 1?
                            group.First()
                            : new PricedProductItem(
                                new ProductItem(productId, group.Sum(pi => pi.Quantity)),
                                unitPrice
                            )
                        )
                        .ToArray()
                },
            ProductItemRemovedFromShoppingCart(_, var pricedProductItem) =>
                shoppingCart with
                {
                    ProductItems = shoppingCart.ProductItems
                        .Select(pi => pi.ProductId == pricedProductItem.ProductId?
                            new PricedProductItem(
                                new ProductItem(pi.ProductId, pi.Quantity - pricedProductItem.Quantity),
                                pi.UnitPrice
                            )
                            :pi
                        )
                        .Where(pi => pi.Quantity > 0)
                        .ToArray()
                },
            ShoppingCartConfirmed(_, var confirmedAt) =>
                shoppingCart with
                {
                    Status = ShoppingCartStatus.Confirmed,
                    ConfirmedAt = confirmedAt
                },
            ShoppingCartCancelled(_, var canceledAt) =>
                shoppingCart with
                {
                    Status = ShoppingCartStatus.Canceled,
                    CanceledAt = canceledAt
                },
            _ => shoppingCart
        };
    }

    [Fact]
    [Trait("Category", "SkipCI")]
    public void GettingState_ForSequenceOfEvents_ShouldSucceed()
    {
        var shoppingCartId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var shoesId = Guid.NewGuid();
        var tShirtId = Guid.NewGuid();
        var twoPairsOfShoes = new PricedProductItem(new ProductItem(shoesId, 2), 100);
        var pairOfShoes = new PricedProductItem(new ProductItem(shoesId, 1), 100);
        var tShirt = new PricedProductItem(new ProductItem(tShirtId, 1), 50);

        var events = new object[]
        {
            // 2. Put your sample events here
            new ShoppingCartOpened(shoppingCartId, clientId),
            new ProductItemAddedToShoppingCart(shoppingCartId, twoPairsOfShoes),
            new ProductItemAddedToShoppingCart(shoppingCartId, tShirt),
            new ProductItemRemovedFromShoppingCart(shoppingCartId, pairOfShoes),
            new ShoppingCartConfirmed(shoppingCartId, DateTime.UtcNow),
            new ShoppingCartCancelled(shoppingCartId, DateTime.UtcNow)
        };

        var shoppingCart = GetShoppingCart(events);

        shoppingCart.Id.Should().Be(shoppingCartId);
        shoppingCart.ClientId.Should().Be(clientId);
        shoppingCart.ProductItems.Should().HaveCount(2);

        shoppingCart.ProductItems[0].Should().Be(pairOfShoes);
        shoppingCart.ProductItems[1].Should().Be(tShirt);
    }
}
