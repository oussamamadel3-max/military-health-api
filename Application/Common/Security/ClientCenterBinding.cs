using Microsoft.AspNetCore.Http;

namespace Application.Common.Security;

public static class ClientCenterBinding
{
    public static Guid? Read(HttpRequest request)
    {
        Guid? seen = null;

        void Consider(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            if (!Guid.TryParse(raw, out var id) || (seen is Guid existing && existing != id))
            {
                seen = Guid.Empty;
                return;
            }

            seen = id;
        }

        if (request.Query.TryGetValue("centerId", out var query))
        {
            foreach (var value in query)
                Consider(value);
        }

        if (request.RouteValues.TryGetValue("centerId", out var route))
            Consider(route?.ToString());

        return seen;
    }
}
