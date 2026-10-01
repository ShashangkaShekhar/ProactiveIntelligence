using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class McpToolArgumentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;

        JsonObject? request;
        try
        {
            request = JsonNode.Parse(body)?.AsObject();
        }
        catch (JsonException)
        {
            await next(context);
            return;
        }

        if (request is null)
        {
            await next(context);
            return;
        }

        var method = request["method"]?.GetValue<string>();
        var parameters = request["params"]?.AsObject();
        var toolName = parameters?["name"]?.GetValue<string>();
        if (!string.Equals(method, "tools/call", StringComparison.Ordinal)
            || !string.Equals(toolName, "run_sql", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var arguments = parameters?["arguments"]?.AsObject();
        if (arguments is null)
        {
            await WriteMissingSqlArgumentAsync(context, request, []);
            return;
        }

        if (arguments["sql"] is null && arguments["query"] is not null)
        {
            arguments["sql"] = arguments["query"]!.DeepClone();
            arguments.Remove("query");
            await RewriteRequestBodyAsync(context, request);
        }
        else if (arguments["sql"] is null)
        {
            await WriteMissingSqlArgumentAsync(context, request, arguments.Select(argument => argument.Key));
            return;
        }

        await next(context);
    }

    private static async Task RewriteRequestBodyAsync(HttpContext context, JsonObject request)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(request);
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
    }

    private static async Task WriteMissingSqlArgumentAsync(
        HttpContext context,
        JsonObject request,
        IEnumerable<string> keys)
    {
        var keyList = string.Join(", ", keys.Select(key => $"'{key}'"));
        var error = $"run_sql requires a 'sql' argument. Received: [{keyList}]";
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = request["id"]?.DeepClone(),
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = error
                    }
                }
            }
        };

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(response.ToJsonString(), context.RequestAborted);
    }
}