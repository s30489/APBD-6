using System.Data;
using APBD_6.DTOs;
using Microsoft.Data.SqlClient;

namespace APBD_6.Services;

public class AppointmentService
{
    private readonly string _connectionString;

    public AppointmentService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    public async Task<List<AppointmentListDto>> GetAllAsync(string? status, string? patientLastName)
    {
        var result = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """, connection);

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value =
            (object?)status ?? DBNull.Value;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 100).Value =
            (object?)patientLastName ?? DBNull.Value;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return result;
    }
    public async Task<ServiceResult<AppointmentDetailsDto>> GetByIdAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment, a.AppointmentDate, a.Status, a.Reason,
                a.InternalNotes, a.CreatedAt,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail, p.PhoneNumber AS PatientPhone,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber AS DoctorLicenseNumber,
                s.Name AS SpecializationName
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """, connection);
    
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
    
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
    
        if (!await reader.ReadAsync())
            return ServiceResult<AppointmentDetailsDto>.NotFound(
                $"Appointment {idAppointment} not found.");
    
        var dto = new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                ? null : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhone = reader.GetString(reader.GetOrdinal("PatientPhone")),
            DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            SpecializationName = reader.GetString(reader.GetOrdinal("SpecializationName"))
        };
    
        return ServiceResult<AppointmentDetailsDto>.Ok(dto);
    }
    
    public async Task<ServiceResult<int>> CreateAsync(CreateAppointmentRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Length > 250)
            return ServiceResult<int>.BadRequest("Reason must be non-empty and at most 250 characters.");
        if (dto.AppointmentDate < DateTime.UtcNow)
            return ServiceResult<int>.BadRequest("Appointment date cannot be in the past.");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        if (!await PatientExistsAndActiveAsync(connection, dto.IdPatient))
            return ServiceResult<int>.BadRequest("Patient does not exist or is inactive.");
        if (!await DoctorExistsAndActiveAsync(connection, dto.IdDoctor))
            return ServiceResult<int>.BadRequest("Doctor does not exist or is inactive.");
        if (await DoctorHasConflictAsync(connection, dto.IdDoctor, dto.AppointmentDate, excludeId: null))
            return ServiceResult<int>.Conflict("Doctor already has an appointment at this time.");
        
        await using var command = new SqlCommand("""
                                                 INSERT INTO dbo.Appointments
                                                     (IdPatient, IdDoctor, AppointmentDate, Status, Reason, CreatedAt)
                                                 OUTPUT INSERTED.IdAppointment
                                                 VALUES
                                                     (@IdPatient, @IdDoctor, @AppointmentDate, 'Scheduled', @Reason, SYSUTCDATETIME());
                                                 """, connection);

        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;

        var newId = (int)(await command.ExecuteScalarAsync())!;
        return ServiceResult<int>.Created(newId);
    }
    
    public async Task<ServiceResult<bool>> UpdateAsync(int idAppointment, UpdateAppointmentRequestDto dto)
{
    var allowedStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
    if (!allowedStatuses.Contains(dto.Status))
        return ServiceResult<bool>.BadRequest("Invalid status.");
    if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Length > 250)
        return ServiceResult<bool>.BadRequest("Reason must be non-empty and at most 250 characters.");

    await using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync();
    
    var current = await GetStatusAndDateAsync(connection, idAppointment);
    if (current is null)
        return ServiceResult<bool>.NotFound($"Appointment {idAppointment} not found.");

    var (currentStatus, currentDate) = current.Value;
    
    if (currentStatus == "Completed" && dto.AppointmentDate != currentDate)
        return ServiceResult<bool>.Conflict("Cannot change date of a completed appointment.");
    
    if (!await PatientExistsAndActiveAsync(connection, dto.IdPatient))
        return ServiceResult<bool>.BadRequest("Patient does not exist or is inactive.");
    if (!await DoctorExistsAndActiveAsync(connection, dto.IdDoctor))
        return ServiceResult<bool>.BadRequest("Doctor does not exist or is inactive.");
    
    if (dto.AppointmentDate != currentDate &&
        await DoctorHasConflictAsync(connection, dto.IdDoctor, dto.AppointmentDate, excludeId: idAppointment))
        return ServiceResult<bool>.Conflict("Doctor already has an appointment at this time.");
    
    await using var command = new SqlCommand("""
        UPDATE dbo.Appointments
        SET IdPatient = @IdPatient,
            IdDoctor = @IdDoctor,
            AppointmentDate = @AppointmentDate,
            Status = @Status,
            Reason = @Reason,
            InternalNotes = @InternalNotes
        WHERE IdAppointment = @IdAppointment;
        """, connection);

    command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
    command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
    command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
    command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = dto.Status;
    command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;
    command.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value =
        (object?)dto.InternalNotes ?? DBNull.Value;
    command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

    await command.ExecuteNonQueryAsync();
    return ServiceResult<bool>.Ok(true);
}
    public async Task<ServiceResult<bool>> DeleteAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var current = await GetStatusAndDateAsync(connection, idAppointment);
        if (current is null)
            return ServiceResult<bool>.NotFound($"Appointment {idAppointment} not found.");
        
        if (current.Value.Status == "Completed")
            return ServiceResult<bool>.Conflict("Cannot delete a completed appointment.");
        
        await using var command = new SqlCommand(
            "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;", connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await command.ExecuteNonQueryAsync();
        return ServiceResult<bool>.Ok(true);
    }
    private static async Task<bool> PatientExistsAndActiveAsync(SqlConnection conn, int idPatient)
    {
        await using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.Patients WHERE IdPatient = @Id AND IsActive = 1;", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = idPatient;
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> DoctorExistsAndActiveAsync(SqlConnection conn, int idDoctor)
    {
        await using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.Doctors WHERE IdDoctor = @Id AND IsActive = 1;", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = idDoctor;
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> DoctorHasConflictAsync(
        SqlConnection conn, int idDoctor, DateTime date, int? excludeId)
    {
        await using var cmd = new SqlCommand("""
                                             SELECT 1 FROM dbo.Appointments
                                             WHERE IdDoctor = @IdDoctor
                                               AND AppointmentDate = @AppointmentDate
                                               AND Status <> 'Cancelled'
                                               AND (@ExcludeId IS NULL OR IdAppointment <> @ExcludeId);
                                             """, conn);
        cmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = idDoctor;
        cmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = date;
        cmd.Parameters.Add("@ExcludeId", SqlDbType.Int).Value = (object?)excludeId ?? DBNull.Value;
        return await cmd.ExecuteScalarAsync() is not null;
    }
    private static async Task<(string Status, DateTime Date)?> GetStatusAndDateAsync(
        SqlConnection conn, int idAppointment)
    {
        await using var cmd = new SqlCommand(
            "SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @Id;", conn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = idAppointment;

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetDateTime(1));
    }
}