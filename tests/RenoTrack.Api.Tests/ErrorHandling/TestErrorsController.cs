using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Application.Common.Exceptions;

namespace RenoTrack.Api.Tests.ErrorHandling;

/// <summary>
/// EXISTS SOLELY TO EXERCISE THE GLOBAL EXCEPTION-HANDLING PIPELINE DURING INTEGRATION TESTS.
/// IT IS NEVER PART OF THE PRODUCTION APPLICATION.
/// </summary>
/// <remarks>
/// <para>
/// Why it lives in the test assembly rather than in RenoTrack.Api behind a conditional: defining
/// it here makes shipping it structurally impossible, not merely unlikely. It reaches the
/// application only because <c>ProblemDetailsExceptionHandlerTests</c> explicitly adds this
/// assembly as an MVC <c>ApplicationPart</c> when building its test host; nothing in
/// <c>Program.cs</c> knows this type exists.
/// </para>
/// <para>
/// Why it exists at all: the middleware must be provable for all seven of its branches, including
/// ones no real endpoint can currently trigger (there are no controllers yet, and no endpoint will
/// ever deliberately throw an unmapped exception). Routing the assertions through a real controller
/// on the real MVC pipeline tests the middleware as it will actually behave in production, rather
/// than unit-testing the handler in isolation with a synthetic HttpContext.
/// </para>
/// <para>
/// Routed under <c>api/test-errors</c>, deliberately <em>not</em> <c>api/v1/...</c>, so it can
/// never collide with a real route or imply it belongs to the versioned public surface (D57).
/// </para>
/// </remarks>
[ApiController]
[Route("api/test-errors")]
public sealed class TestErrorsController : ControllerBase
{
    [HttpGet("not-found")]
    public IActionResult NotFound_() => throw new NotFoundException("Lead", 42);

    [HttpGet("forbidden")]
    public IActionResult Forbidden_() =>
        throw new ForbiddenException("Inspection 7 is not assigned to Inspector 3.");

    [HttpGet("conflict")]
    public IActionResult Conflict_() =>
        throw new ConflictException("Lead 42 already has an active Angebot.");

    [HttpGet("validation")]
    public IActionResult Validation() => throw new ValidationException(
    [
        new ValidationFailure("Name", "Name is required."),
        new ValidationFailure("Email", "Email must be a valid email address."),
        new ValidationFailure("Name", "Name must not exceed 200 characters."),
    ]);

    [HttpGet("argument")]
    public IActionResult Argument() =>
        throw new ArgumentException("Quantity must be greater than zero.", "quantity");

    [HttpGet("invalid-operation")]
    public IActionResult InvalidOperation() =>
        throw new InvalidOperationException("Cannot submit an Angebot in status Sent for review.");

    /// <summary>
    /// Stands in for a genuinely unexpected fault. The message is deliberately something that would
    /// be damaging to leak, so the test asserting it never reaches the client is meaningful.
    /// </summary>
    [HttpGet("unmapped")]
    public IActionResult Unmapped() =>
        throw new DataMisalignedException("Server=secret-host;Password=hunter2");
}
