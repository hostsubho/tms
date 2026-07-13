using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Onboarding;

namespace Tms.Api.Controllers;

// Public (no auth) - the signup wizard needs to show pricing/plan options
// before anyone has an account. No write endpoints here yet; plans are
// seeded directly (docs/seed-plans.sql) until Super Admin billing (Module 5.2)
// gets a real admin UI for managing them.
[ApiController]
[Route("api/plans")]
public class PlansController : ControllerBase
{
    private readonly TmsDbContext _db;

    public PlansController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlanResponse>>> GetPlans(CancellationToken ct)
    {
        var plans = await _db.Plans
            .OrderBy(p => p.PriceMonthly)
            .Select(p => new PlanResponse(p.Id, p.Name, p.MaxAgents, p.MaxTicketsPerMonth, p.PriceMonthly))
            .ToListAsync(ct);

        return Ok(plans);
    }
}
