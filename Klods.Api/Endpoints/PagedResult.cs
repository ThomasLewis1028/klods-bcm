namespace Klods.Api.Endpoints;

public record PagedResult<T>(List<T> Items, bool HasMore);
