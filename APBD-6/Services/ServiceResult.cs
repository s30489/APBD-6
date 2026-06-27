namespace APBD_6.Services;

public enum ResultStatus { Ok, NotFound, BadRequest, Conflict, Created }

public class ServiceResult<T>
{
    public ResultStatus Status { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }

    public static ServiceResult<T> Ok(T value) => new() { Status = ResultStatus.Ok, Value = value };
    public static ServiceResult<T> Created(T value) => new() { Status = ResultStatus.Created, Value = value };
    public static ServiceResult<T> NotFound(string msg) => new() { Status = ResultStatus.NotFound, Error = msg };
    public static ServiceResult<T> BadRequest(string msg) => new() { Status = ResultStatus.BadRequest, Error = msg };
    public static ServiceResult<T> Conflict(string msg) => new() { Status = ResultStatus.Conflict, Error = msg };
}