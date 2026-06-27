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
    
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentRequestDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return result.Status switch
        {
            ResultStatus.Created => CreatedAtAction(
                nameof(GetById),
                new { idAppointment = result.Value },
                new { idAppointment = result.Value }),
            ResultStatus.BadRequest => BadRequest(new ErrorResponseDto(result.Error!)),
            ResultStatus.Conflict => Conflict(new ErrorResponseDto(result.Error!)),
            _ => StatusCode(500)
        };
    }
    [HttpPut("{idAppointment:int}")]
    public async Task<IActionResult> Update(
        int idAppointment, [FromBody] UpdateAppointmentRequestDto dto)
    {
        var result = await _service.UpdateAsync(idAppointment, dto);
        return result.Status switch
        {
            ResultStatus.Ok => Ok(),
            ResultStatus.NotFound => NotFound(new ErrorResponseDto(result.Error!)),
            ResultStatus.BadRequest => BadRequest(new ErrorResponseDto(result.Error!)),
            ResultStatus.Conflict => Conflict(new ErrorResponseDto(result.Error!)),
            _ => StatusCode(500)
        };
    }
}