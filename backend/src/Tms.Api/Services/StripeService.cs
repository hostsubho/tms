using Stripe;
using Stripe.Checkout;

namespace Tms.Api.Services;

// Module 5.2 - Plans & Billing Administration. Reads Stripe:SecretKey /
// Stripe:WebhookSecret lazily, per call, rather than at app startup like
// Auth:SigningKey does - unlike the signing key, Stripe isn't load-bearing
// for the rest of the app (tickets, auth, everything built in prior
// modules works with zero Stripe configuration), so a tenant admin hitting
// a billing endpoint before Ron has configured Stripe should get a clear
// 500 from *that* request, not have the entire API refuse to start.
public class StripeService : IStripeService
{
    private readonly IConfiguration _config;

    public StripeService(IConfiguration config)
    {
        _config = config;
    }

    private string SecretKey => _config["Stripe:SecretKey"]
        ?? throw new InvalidOperationException(
            "Missing Stripe:SecretKey. Set it via user-secrets locally or the App Service/Render configuration in prod.");

    private string WebhookSecret => _config["Stripe:WebhookSecret"]
        ?? throw new InvalidOperationException(
            "Missing Stripe:WebhookSecret. Set it via user-secrets locally or the App Service/Render configuration in prod.");

    public async Task<string> CreateCustomerAsync(Guid tenantId, string name, string adminEmail, CancellationToken ct)
    {
        var service = new CustomerService(new StripeClient(SecretKey));
        var options = new CustomerCreateOptions
        {
            Name = name,
            Email = adminEmail,
            Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId.ToString() },
        };
        var customer = await service.CreateAsync(options, cancellationToken: ct);
        return customer.Id;
    }

    public async Task<string> CreateCheckoutSessionAsync(
        Guid tenantId, string stripeCustomerId, string stripePriceId, string successUrl, string cancelUrl, CancellationToken ct)
    {
        var service = new SessionService(new StripeClient(SecretKey));
        var options = new SessionCreateOptions
        {
            Customer = stripeCustomerId,
            Mode = "subscription",
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Price = stripePriceId, Quantity = 1 },
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            // Read back in StripeWebhookController's checkout.session.completed
            // handler - the session itself doesn't otherwise carry which
            // tenant/Plan this was for.
            Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId.ToString() },
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId.ToString() },
            },
        };
        var session = await service.CreateAsync(options, cancellationToken: ct);
        return session.Url;
    }

    public async Task<(string? PriceId, DateTime? CurrentPeriodEnd)> GetSubscriptionDetailsAsync(string stripeSubscriptionId, CancellationToken ct)
    {
        var service = new SubscriptionService(new StripeClient(SecretKey));
        var subscription = await service.GetAsync(stripeSubscriptionId, cancellationToken: ct);
        var priceId = subscription.Items.Data.FirstOrDefault()?.Price?.Id;
        return (priceId, subscription.CurrentPeriodEnd);
    }

    public async Task UpdateSubscriptionPriceAsync(string stripeSubscriptionId, string newStripePriceId, CancellationToken ct)
    {
        var service = new SubscriptionService(new StripeClient(SecretKey));
        var subscription = await service.GetAsync(stripeSubscriptionId, cancellationToken: ct);
        var currentItem = subscription.Items.Data.FirstOrDefault()
            ?? throw new InvalidOperationException($"Stripe subscription {stripeSubscriptionId} has no line items to update.");

        var options = new SubscriptionUpdateOptions
        {
            Items = new List<SubscriptionItemOptions>
            {
                new() { Id = currentItem.Id, Price = newStripePriceId },
            },
            // Stripe's default - immediately charges/credits the prorated
            // difference on the next invoice rather than waiting for the
            // current period to end.
            ProrationBehavior = "create_prorations",
        };
        await service.UpdateAsync(stripeSubscriptionId, options, cancellationToken: ct);
    }

    public async Task CancelSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct)
    {
        var service = new SubscriptionService(new StripeClient(SecretKey));
        await service.CancelAsync(stripeSubscriptionId, cancellationToken: ct);
    }

    public async Task<string> CreateBillingPortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct)
    {
        var service = new Stripe.BillingPortal.SessionService(new StripeClient(SecretKey));
        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = stripeCustomerId,
            ReturnUrl = returnUrl,
        };
        var session = await service.CreateAsync(options, cancellationToken: ct);
        return session.Url;
    }

    public async Task ApplyCreditAsync(string stripeCustomerId, long amountCents, string reason, CancellationToken ct)
    {
        var service = new CustomerBalanceTransactionService(new StripeClient(SecretKey));
        var options = new CustomerBalanceTransactionCreateOptions
        {
            // Negative = reduces the customer's balance owed (a credit).
            // A positive amount here would instead add a charge to their
            // next invoice - callers of ApplyCreditAsync always mean the
            // former, so the negation happens here, once, rather than
            // trusting every call site to remember the sign convention.
            Amount = -Math.Abs(amountCents),
            Currency = "usd",
            Description = reason,
        };
        await service.CreateAsync(stripeCustomerId, options, cancellationToken: ct);
    }

    public Event ConstructWebhookEvent(string json, string signatureHeader)
    {
        return EventUtility.ConstructEvent(json, signatureHeader, WebhookSecret);
    }
}
