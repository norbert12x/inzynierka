using Inżynierka.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inżynierka.Controllers;

[ApiController]
[Route("api/attraction-categories")]
public class AttractionCategoriesController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AttractionCategory>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AttractionCategory>> Get()
    {
        return Ok(AttractionCategoryCatalog.All);
    }
}
