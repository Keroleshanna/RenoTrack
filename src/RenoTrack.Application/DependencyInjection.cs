using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Application.Angebote.Commands.AddAngebotItem;
using RenoTrack.Application.Angebote.Commands.AddAngebotSection;
using RenoTrack.Application.Angebote.Commands.ApproveAngebot;
using RenoTrack.Application.Angebote.Commands.CreateAngebot;
using RenoTrack.Application.Angebote.Commands.RequestAngebotChanges;
using RenoTrack.Application.Angebote.Commands.SubmitAngebotForReview;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.CatalogItems.Commands.CreateCatalogItem;
using RenoTrack.Application.CatalogItems.Commands.RetireCatalogItem;
using RenoTrack.Application.CatalogItems.Commands.UpdateCatalogItem;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.CatalogItems.Queries.SearchCatalogItems;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Inspections.Commands.CompleteInspection;
using RenoTrack.Application.Inspections.Commands.ScheduleInspection;
using RenoTrack.Application.Inspections.Commands.UpdateInspectionNotes;
using RenoTrack.Application.Inspections.Commands.UploadInspectionPhoto;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Application.Leads.Commands.CreateLead;
using RenoTrack.Application.Leads.Dtos;

namespace RenoTrack.Application;

/// <summary>
/// The composition root for the Application layer. It registers Application services only —
/// validators, command/query handlers, and services whose implementation lives in this project —
/// and depends solely on dependency-injection abstractions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Do not add hosting or configuration concerns here.</b> This file must not take an
/// <c>IConfiguration</c>, read environment variables, touch the file system, or reference anything
/// from ASP.NET Core or the generic host. Those belong to <c>AddInfrastructure()</c> (which does
/// legitimately need configuration, for the connection string) or to <c>Program.cs</c>. Keeping
/// this layer's only package dependencies to FluentValidation and
/// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> is what keeps the Application layer
/// testable without a host, which is the entire reason <c>RenoTrack.Application.Tests</c> can run
/// against hand-written fakes with no framework involved.
/// </para>
/// <para>
/// <b>Every registration is explicit — no assembly scanning.</b> This is deliberate and matches
/// <c>AddInfrastructure()</c>: the list doubles as a readable inventory of every use case the
/// application supports, and it keeps reflective magic out of production code, consistent with this
/// project's rejection of MediatR (D22) and of generic catch-all abstractions (D28). The obvious
/// risk — adding a handler and forgetting to register it — is covered instead by
/// <c>RenoTrack.Api.Tests</c>' <c>DependencyInjectionTests</c>, which reflects over this assembly
/// and asserts that every handler and validator it finds actually resolves. Reflection in the test,
/// explicit registration in production (CLAUDE.md §14 sanctions the former, not the latter).
/// </para>
/// <para>
/// <b>Every registration is Scoped</b>, matching <c>AddInfrastructure()</c>'s uniform lifetime rule.
/// Handlers must be Scoped because they depend on Scoped repositories and the Scoped
/// <c>DbContext</c>. Validators and <c>OwnershipValidator</c> are stateless and would be safe as
/// Singletons today, but one uniform rule removes a whole class of captive-dependency mistakes
/// before it can happen — the same reasoning that kept Infrastructure's dependency-free
/// placeholders Scoped rather than Singleton (D48).
/// </para>
/// <para>
/// <b>Ordering rule, applied identically in every category below:</b> registrations are grouped by
/// feature in business-workflow order — Lead, Inspection, Angebot, CatalogItem — and within each
/// feature they follow that feature's own workflow order (e.g. an Angebot is created, then built
/// up with sections and items, then submitted, then approved or returned). Deliberately not
/// alphabetical: reading this file top to bottom should trace the same path a Lead takes through
/// the business. Keep any new registration in its feature's group, in workflow position; do not
/// append to the end of a category.
/// </para>
/// <para>
/// Handlers are registered <em>by their interface</em>
/// (<see cref="ICommandHandler{TCommand,TResult}"/> / <see cref="IQueryHandler{TQuery,TResult}"/>),
/// not as concrete types, so controllers depend on the Application abstraction rather than on an
/// implementation — which is what keeps that interface load-bearing instead of a decorative marker
/// every handler implements for no runtime purpose (CLAUDE.md §3).
/// </para>
/// </remarks>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        AddValidators(services);
        AddCommandHandlers(services);
        AddQueryHandlers(services);
        AddServices(services);

        return services;
    }

    /// <summary>
    /// Shape-only request validation (CLAUDE.md §5) — never business rules, never a repository
    /// call. One per command that has parameters worth checking; <c>SearchCatalogItemsQuery</c>
    /// deliberately has none, because it takes no parameters at all (D37) — which is why this
    /// category has 14 entries where the handler categories together have 15.
    /// Ordering follows the file-level rule above.
    /// </summary>
    private static void AddValidators(IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateLeadCommand>, CreateLeadCommandValidator>();

        services.AddScoped<IValidator<ScheduleInspectionCommand>, ScheduleInspectionCommandValidator>();
        services.AddScoped<IValidator<CompleteInspectionCommand>, CompleteInspectionCommandValidator>();
        services.AddScoped<IValidator<UploadInspectionPhotoCommand>, UploadInspectionPhotoCommandValidator>();
        services.AddScoped<IValidator<UpdateInspectionNotesCommand>, UpdateInspectionNotesCommandValidator>();

        services.AddScoped<IValidator<CreateAngebotCommand>, CreateAngebotCommandValidator>();
        services.AddScoped<IValidator<AddAngebotSectionCommand>, AddAngebotSectionCommandValidator>();
        services.AddScoped<IValidator<AddAngebotItemCommand>, AddAngebotItemCommandValidator>();
        services.AddScoped<IValidator<SubmitAngebotForReviewCommand>, SubmitAngebotForReviewCommandValidator>();
        services.AddScoped<IValidator<ApproveAngebotCommand>, ApproveAngebotCommandValidator>();
        services.AddScoped<IValidator<RequestAngebotChangesCommand>, RequestAngebotChangesCommandValidator>();

        services.AddScoped<IValidator<CreateCatalogItemCommand>, CreateCatalogItemCommandValidator>();
        services.AddScoped<IValidator<UpdateCatalogItemCommand>, UpdateCatalogItemCommandValidator>();
        services.AddScoped<IValidator<RetireCatalogItemCommand>, RetireCatalogItemCommandValidator>();
    }

    /// <summary>
    /// One hand-written handler per command, called directly — no mediator, no pipeline behaviors
    /// (CLAUDE.md §3, D22). Ordering follows the file-level rule above, and deliberately mirrors
    /// <see cref="AddValidators"/> entry for entry, so the two lists can be diffed by eye.
    /// </summary>
    private static void AddCommandHandlers(IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<CreateLeadCommand, LeadDto>, CreateLeadCommandHandler>();

        services.AddScoped<ICommandHandler<ScheduleInspectionCommand, InspectionDto>, ScheduleInspectionCommandHandler>();
        services.AddScoped<ICommandHandler<CompleteInspectionCommand, InspectionDto>, CompleteInspectionCommandHandler>();
        services.AddScoped<ICommandHandler<UploadInspectionPhotoCommand, PhotoDto>, UploadInspectionPhotoCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateInspectionNotesCommand, InspectionDto>, UpdateInspectionNotesCommandHandler>();

        services.AddScoped<ICommandHandler<CreateAngebotCommand, AngebotDto>, CreateAngebotCommandHandler>();
        services.AddScoped<ICommandHandler<AddAngebotSectionCommand, SectionDto>, AddAngebotSectionCommandHandler>();
        services.AddScoped<ICommandHandler<AddAngebotItemCommand, AddAngebotItemResult>, AddAngebotItemCommandHandler>();
        services.AddScoped<ICommandHandler<SubmitAngebotForReviewCommand, AngebotDto>, SubmitAngebotForReviewCommandHandler>();
        services.AddScoped<ICommandHandler<ApproveAngebotCommand, AngebotDto>, ApproveAngebotCommandHandler>();
        services.AddScoped<ICommandHandler<RequestAngebotChangesCommand, AngebotDto>, RequestAngebotChangesCommandHandler>();

        services.AddScoped<ICommandHandler<CreateCatalogItemCommand, CatalogItemDto>, CreateCatalogItemCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateCatalogItemCommand, CatalogItemDto>, UpdateCatalogItemCommandHandler>();
        services.AddScoped<ICommandHandler<RetireCatalogItemCommand, CatalogItemDto>, RetireCatalogItemCommandHandler>();
    }

    /// <summary>
    /// Queries get their own dispatch abstraction rather than reusing <c>ICommandHandler</c>, even
    /// though the signatures currently coincide (D36). Only one query exists so far.
    /// </summary>
    private static void AddQueryHandlers(IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<SearchCatalogItemsQuery, IReadOnlyList<CatalogItemDto>>, SearchCatalogItemsQueryHandler>();
    }

    /// <summary>
    /// Application-layer service implementations. <c>OwnershipValidator</c> is the only one:
    /// unlike every other service interface, it has no external dependency (no EF Core, no disk, no
    /// network) that would justify an Infrastructure-side implementation, so it lives here and is
    /// deliberately excluded from <c>AddInfrastructure()</c> (CLAUDE.md §9).
    /// </summary>
    private static void AddServices(IServiceCollection services)
    {
        services.AddScoped<IOwnershipValidator, OwnershipValidator>();
    }
}
