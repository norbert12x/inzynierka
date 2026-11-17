using Inżynierka.Configuration;
using Inżynierka.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Inżynierka.Controllers;

[ApiController]
[Route("api/regions")]
public class RegionsController(IOptions<CatalogOptions> options) : ControllerBase
{
    // Kolejność jak w konfiguracji. Nie wymaga bazy ani zewnętrznych usług.
    [HttpGet]
    [ProducesResponseType(typeof(RegionListResponse), StatusCodes.Status200OK)]
    public ActionResult<RegionListResponse> Get()
    {
        var items = options.Value.Regions
            .Select(r => new RegionResponse(r.Code, r.Name, r.CenterLatitude, r.CenterLongitude, r.RadiusKm, r.IsDefault))
            .ToList();
        return Ok(new RegionListResponse(items));
    }
}
