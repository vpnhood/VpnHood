using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Mvc;

public static class MvcRouter
{
    public static void RegisterRoutes(WebserverBase server, Assembly assembly)
    {
        var controllerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"))
            .ToArray();

        foreach (var controllerType in controllerTypes) {
            var controllerName = controllerType.Name.Replace("Controller", "");
            var controllerRouteAttr = controllerType.GetCustomAttribute<RouteAttribute>();
            var controllerRoute = controllerRouteAttr?.Template ?? controllerName.ToLowerInvariant();

            var actionMethods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttributes<RouteAttribute>().Any())
                .ToArray();

            foreach (var methodInfo in actionMethods) {
                var routeAttributes = methodInfo.GetCustomAttributes<RouteAttribute>().ToArray();

                foreach (var routeAttr in routeAttributes) {
                    var httpMethod = GetHttpMethodFromAttribute(routeAttr);
                    if (httpMethod == null) continue;

                    var actionRoute = routeAttr.Template;
                    var fullRoute = CombineRoutes(controllerRoute, actionRoute);

                    // Determine if route has parameters
                    var hasParameters = fullRoute.Contains('{');

                    Func<HttpContextBase, Task> handler = async (ctx) => {
                        try {
                            // Add CORS headers centrally
                            CorsMiddleware.AddCors(ctx);

                            var controller = Activator.CreateInstance(controllerType);
                            var parameters = methodInfo.GetParameters();
                            var methodParameters = new object[parameters.Length];

                            // Handle parameters with improved binding
                            for (int i = 0; i < parameters.Length; i++) {
                                var param = parameters[i];
                                var paramValue = await GetParameterValue(ctx, param);
                                methodParameters[i] = paramValue;
                            }

                            // Invoke the action method
                            var result = methodInfo.Invoke(controller, methodParameters);

                            // Handle different return types
                            if (result is Task task) {
                                await task;

                                // Check if it's Task<T>
                                if (task.GetType().IsGenericType) {
                                    var resultProperty = task.GetType().GetProperty("Result");
                                    var taskResult = resultProperty?.GetValue(task);

                                    if (taskResult != null && !IsVoidResult(taskResult)) {
                                        await ctx.SendJson(taskResult);
                                    }
                                }
                            }
                            else if (result != null && !IsVoidResult(result)) {
                                await ctx.SendJson(result);
                            }
                        }
                        catch (TargetParameterCountException) {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.Send("Bad Request: Parameter mismatch");
                        }
                        catch (ArgumentException ex) {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.Send($"Bad Request: {ex.Message}");
                        }
                        catch (Exception ex) {
                            ctx.Response.StatusCode = 500;
                            await ctx.SendJson(new { error = ex.Message, type = ex.GetType().Name }, 500);
                        }
                    };

                    // Register the route
                    if (hasParameters) {
                        server.Routes.PreAuthentication.Parameter.Add(httpMethod.Value, fullRoute, handler);
                    }
                    else {
                        server.Routes.PreAuthentication.Static.Add(httpMethod.Value, fullRoute, handler);
                    }

                    // Add OPTIONS handler for CORS preflight - with centralized CORS
                    Func<HttpContextBase, Task> optionsHandler = async ctx => {
                        CorsMiddleware.AddCors(ctx);
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.Send();
                    };

                    if (hasParameters) {
                        server.Routes.PreAuthentication.Parameter.Add(HttpMethod.OPTIONS, fullRoute, optionsHandler);
                    }
                    else {
                        server.Routes.PreAuthentication.Static.Add(HttpMethod.OPTIONS, fullRoute, optionsHandler);
                    }
                }
            }
        }
    }

    private static async Task<object?> GetParameterValue(HttpContextBase ctx, ParameterInfo param)
    {
        var paramName = param.Name ?? "";
        var paramType = param.ParameterType;

        // Handle special types
        if (paramType == typeof(HttpContextBase)) {
            return ctx;
        }

        // Handle complex types (from request body)
        if (IsComplexType(paramType)) {
            if (ctx.Request.ContentLength > 0) {
                try {
                    return ctx.ReadJson(paramType);
                }
                catch {
                    if (param.HasDefaultValue)
                        return param.DefaultValue;

                    if (paramType.IsValueType)
                        return Activator.CreateInstance(paramType);

                    return null;
                }
            }

            if (param.HasDefaultValue)
                return param.DefaultValue;

            if (paramType.IsValueType)
                return Activator.CreateInstance(paramType);

            return null;
        }

        // Try route parameters first
        var routeValue = ctx.GetRouteParameter(paramName);
        if (routeValue != null) {
            return ConvertValue(routeValue, paramType, param);
        }

        // Try query parameters
        var queryValue = ctx.GetQueryParameterString(paramName);
        if (queryValue != null) {
            return ConvertValue(queryValue, paramType, param);
        }

        // Handle default values
        if (param.HasDefaultValue) {
            return param.DefaultValue;
        }

        // Handle nullable types
        if (IsNullableType(paramType)) {
            return null;
        }

        // For required parameters, throw exception
        throw new ArgumentException($"Missing required parameter '{paramName}'");
    }

    private static object ReadJson(this HttpContextBase ctx, Type type)
    {
        var method = typeof(HttpContextBaseExtensions)
            .GetMethod(nameof(HttpContextBaseExtensions.ReadJson))!
            .MakeGenericMethod(type);
        
        return method.Invoke(null, new object[] { ctx })!;
    }

    private static object? ConvertValue(string value, Type targetType, ParameterInfo param)
    {
        try {
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlyingType == typeof(Guid)) {
                return Guid.Parse(value);
            }

            if (underlyingType.IsEnum) {
                return Enum.Parse(underlyingType, value, true);
            }

            if (underlyingType == typeof(bool)) {
                return bool.Parse(value);
            }

            if (underlyingType == typeof(DateTime)) {
                return DateTime.Parse(value);
            }

            if (underlyingType == typeof(DateTimeOffset)) {
                return DateTimeOffset.Parse(value);
            }

            if (underlyingType == typeof(TimeSpan)) {
                return TimeSpan.Parse(value);
            }

            // Use TypeConverter for complex conversions
            var converter = TypeDescriptor.GetConverter(underlyingType);
            if (converter.CanConvertFrom(typeof(string))) {
                return converter.ConvertFromString(value);
            }

            return Convert.ChangeType(value, underlyingType);
        }
        catch {
            if (param.HasDefaultValue)
                return param.DefaultValue;

            if (IsNullableType(targetType))
                return null;

            throw new ArgumentException($"Cannot convert '{value}' to {targetType.Name} for parameter '{param.Name}'");
        }
    }

    private static bool IsComplexType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return !underlyingType.IsPrimitive
               && underlyingType != typeof(string)
               && underlyingType != typeof(decimal)
               && underlyingType != typeof(DateTime)
               && underlyingType != typeof(DateTimeOffset)
               && underlyingType != typeof(TimeSpan)
               && underlyingType != typeof(Guid)
               && !underlyingType.IsEnum;
    }

    private static bool IsNullableType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    private static bool IsVoidResult(object result)
    {
        return result is Unit || result.GetType().Name == "VoidTaskResult";
    }

    private static HttpMethod? GetHttpMethodFromAttribute(RouteAttribute attribute)
    {
        return attribute switch {
            HttpGetAttribute => HttpMethod.GET,
            HttpPostAttribute => HttpMethod.POST,
            HttpPutAttribute => HttpMethod.PUT,
            HttpDeleteAttribute => HttpMethod.DELETE,
            _ => null
        };
    }

    private static string CombineRoutes(string prefix, string suffix)
    {
        if (string.IsNullOrEmpty(prefix)) return "/" + suffix.TrimStart('/');
        if (string.IsNullOrEmpty(suffix)) return "/" + prefix.TrimStart('/');
        return "/" + prefix.Trim('/') + "/" + suffix.TrimStart('/');
    }
}

// Helper type for void results
public struct Unit
{
    public static readonly Unit Value = new();
}
