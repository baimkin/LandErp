# TP-004 — Quality gates и базовая наблюдаемость

**Статус:** На согласовании  
**Зависимости:** TP-001–TP-003, FP-001  
**Результат:** архитектурные ошибки и неработоспособность hosts обнаруживаются до продуктовой разработки.

## Входит

- unit, integration и architecture test foundations;
- тесты project/namespace boundaries без лишнего framework при возможности;
- Problem Details для API;
- correlation ID через HTTP и Worker scope;
- readiness/liveness health endpoints;
- структурированные встроенные логи;
- локальный скрипт последовательных проверок и будущий CI contract.

## Эталон correlation middleware

```csharp
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var value)
            ? value.ToString()
            : Guid.CreateVersion7().ToString();

        context.Response.Headers["X-Correlation-ID"] = correlationId;
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await next(context);
        }
    }
}
```

В реализации дополнительно валидируется длина/формат входного header.

## Обязательные проверки

- запрещённый project reference ломает architecture test;
- Server и Worker запускаются независимо;
- readiness отражает PostgreSQL, liveness не падает из-за временной внешней зависимости;
- API error возвращает Problem Details и correlation ID;
- логи не содержат секретов;
- format/build/test выполняются отдельными одобряемыми командами.

## Definition of Done

Есть единая локальная последовательность quality gates, минимум один тест каждого
заявленного уровня и документированное различие readiness/liveness.

