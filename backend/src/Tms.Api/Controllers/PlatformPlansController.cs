using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Platform;
using Tms.Api.Models;

namespace Tms.Api.Controllers;

// Module 5.2 - Plans & Billing Administration: "Define/edit plans" bar.
// Read is open to any platform role (PlatformAdmin policy, same as
// SuperAdminTenantsController's GETs); create/update restricted to
// PlatformManage (Owner/PlatformAdmin only) - editing what a plan costs or
// includes is a pricing decision, not a day-to-day billing-ops one, so it
// sits with the stricter policy rather than PlatformBilling.
[ApiController]
[Route("api/platform/plans")]
[Authorize(Policy = "PlatformAdmin")]
public class PlatformPlansController : ControllerBase
{
    private readonly TmsDbContext _db;

    public PlatformPlansController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlatformPlanResponse>>> GetPlans(CancellationToken ct)
    {
        var plans = await _db.Plans.OrderBy(p => p.PriceMonthly).ToListAsync(ct);
        return Ok(plans.Select(PlatformPlanResponse.FromEntity));
    }

    [HttpPost]
    [Authorize(Policy = "PlatformManage")]
    public async Task<ActionResult<PlatformPlanResponse>> CreatePlan([FromBody] CreatePlanRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required." });
        }
        if (request.PriceMonthly < 0 || request.MaxAgents < 0 || request.MaxTicketsPerMonth < 0)
        {
            return BadRequest(new { message = "priceMonthly, maxAgents, and maxTicketsPerMonth must not be negative." });
        }

        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            MaxAgents = request.MaxAgents,
            MaxTicketsPerMonth = request.MaxTicketsPerMonth,
            PriceMonthly = request.PriceMonthly,
            StripePriceId = request.StripePriceId,
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetPlans), PlatformPlanResponse.FromEntity(plan));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "PlatformManage")]
    public async Task<ActionResult<PlatformPlanResponse>> UpdatePlan(Guid id, [FromBody] UpdatePlanRequest request, CancellationToken ct)
    {
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return NotFound();

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message = "Name cannot be blank." });
            plan.Name = request.Name;
        }
        if (request.MaxAgents is not null)
        {
            if (request.MaxAgents < 0) return BadRequest(new { message = "maxAgents must not be negative." });
            plan.MaxAgents = request.MaxAgents.Value;
        }
        if (request.MaxTicketsPerMonth is not null)
        {
            if (request.MaxTicketsPerMonth < 0) return BadRequest(new { message = "maxTicketsPerMonth must not be negative." });
            plan.MaxTicketsPerMonth = request.MaxTicketsPerMonth.Value;
        }
        if (request.PriceMonthly is not null)
        {
            if (request.PriceMonthly < 0) return BadRequest(new { message = "priceMonthly must not be negative." });
            plan.PriceMonthly = request.PriceMonthly.Value;
        }
        // StripePriceId is the one field this endpoint exists for above and
        // beyond raw SQL - set once Ron creates the matching Stripe Price in
        // the Dashboard/API. Empty string clears it (falls back to no
        // billing wired up for that plan), null leaves it untouched.
        if (request.StripePriceId is not null)
        {
            plan.StripePriceId = request.StripePriceId.Length == 0 ? null : request.StripePriceId;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(PlatformPlanResponse.FromEntity(plan));
    }
}
