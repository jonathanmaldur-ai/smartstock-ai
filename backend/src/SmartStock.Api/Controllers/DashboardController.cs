using Microsoft.AspNetCore.Mvc;
using SmartStock.Application.Analysis;

namespace SmartStock.Api.Controllers;

/// <summary>Dashboard executivo (Módulo 2.4): consulta para todos os perfis.</summary>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public Task<DashboardDto> Get(CancellationToken cancellationToken) => dashboard.GetAsync(cancellationToken);
}
