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
    
    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetById(int idAppointment)
    {
        var result = await _service.GetByIdAsync(idAppointment);
        return result.Status == ResultStatus.NotFound
            ? NotFound(new ErrorResponseDto(result.Error!))
            : Ok(result.Value);
    }
}