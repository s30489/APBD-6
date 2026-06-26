using APBD_6.DTOs;
using APBD_6.Services;
using Microsoft.AspNetCore.Mvc;

namespace APBD_6.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly AppointmentService _service;

    public AppointmentsController(AppointmentService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        var items = await _service.GetAllAsync(status, patientLastName);
        return Ok(items);
    }
}