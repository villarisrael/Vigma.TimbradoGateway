using Microsoft.AspNetCore.Mvc;
using Vigma.TimbradoGateway.Infrastructure;

namespace Vigma.TimbradoGateway.Controllers;

[ApiController]
[Route("api/testdb")]
public class TestDbController : ControllerBase
{
    private readonly TimbradoDbContext _db;
    public TestDbController(TimbradoDbContext db) => _db = db;

    [HttpGet]
    public IActionResult Get()
    {
        var tenants = _db.Tenants.Count();
        return Ok(new { ok = true, tenants });
    }
}
