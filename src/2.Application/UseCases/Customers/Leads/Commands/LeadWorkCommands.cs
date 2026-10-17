using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Catalog;
using JOIN.Domain.Customers.Leads;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Leads.Commands;

/// <summary><c>POST /Leads/{id}/interactions</c> (SPEC 61 C.29): only open leads; moves <c>LastInteractionAt</c>.</summary>
public sealed record AddLeadInteractionCommand : ITransactionalCommand<Response<LeadInteractionDto>>
{
    public Guid LeadId { get; init; }
    public LeadInteractionInput Interaction { get; init; } = new();
}

/// <summary><c>PUT /Leads/{id}/interactions/{interactionId}</c>: only its author, within 24 hours.</summary>
public sealed record UpdateLeadInteractionCommand : ITransactionalCommand<Response<LeadInteractionDto>>
{
    public Guid LeadId { get; init; }
    public Guid InteractionId { get; init; }
    public LeadInteractionInput Interaction { get; init; } = new();
}

/// <summary><c>POST /Leads/{id}/comments</c> (SPEC 61 C.30).</summary>
public sealed record AddLeadCommentCommand : ITransactionalCommand<Response<LeadCommentDto>>
{
    public Guid LeadId { get; init; }
    public Guid? LeadInteractionId { get; init; }
    public string Body { get; init; } = string.Empty;
}

/// <summary><c>PUT /Leads/{id}/comments/{commentId}</c>: only its author.</summary>
public sealed record UpdateLeadCommentCommand : ITransactionalCommand<Response<LeadCommentDto>>
{
    public Guid LeadId { get; init; }
    public Guid CommentId { get; init; }
    public string Body { get; init; } = string.Empty;
}

/// <summary><c>DELETE /Leads/{id}/comments/{commentId}</c>: logical delete, only its author.</summary>
public sealed record DeleteLeadCommentCommand(Guid LeadId, Guid CommentId) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /Leads/{id}/products</c> (SPEC 61 C.31): only open leads; recalculates the value.</summary>
public sealed record AddLeadProductCommand : ITransactionalCommand<Response<LeadProductDto>>
{
    public Guid LeadId { get; init; }
    public LeadProductInput Line { get; init; } = new();
}

/// <summary><c>PUT /Leads/{id}/products/{lineId}</c>: quantity, price, discount and status.</summary>
public sealed record UpdateLeadProductCommand : ITransactionalCommand<Response<LeadProductDto>>
{
    public Guid LeadId { get; init; }
    public Guid LineId { get; init; }
    public decimal Quantity { get; init; } = 1m;
    public decimal UnitPrice { get; init; }
    public decimal DiscountPercent { get; init; }
    /// <summary>1 Offered, 2 Accepted, 3 Rejected.</summary>
    public int Status { get; init; } = 1;
}

/// <summary><c>DELETE /Leads/{id}/products/{lineId}</c>: logical delete; recalculates the value.</summary>
public sealed record DeleteLeadProductCommand(Guid LeadId, Guid LineId) : ITransactionalCommand<Response<Guid>>;

/// <summary>Validates <see cref="AddLeadInteractionCommand"/>.</summary>
public sealed class AddLeadInteractionCommandValidator : AbstractValidator<AddLeadInteractionCommand>
{
    public AddLeadInteractionCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.Interaction).SetValidator(new LeadInteractionInputValidator());
    }
}

/// <summary>Validates <see cref="UpdateLeadInteractionCommand"/>.</summary>
public sealed class UpdateLeadInteractionCommandValidator : AbstractValidator<UpdateLeadInteractionCommand>
{
    public UpdateLeadInteractionCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.InteractionId).NotEmpty();
        RuleFor(x => x.Interaction).SetValidator(new LeadInteractionInputValidator());
    }
}

/// <summary>Validates <see cref="AddLeadCommentCommand"/>.</summary>
public sealed class AddLeadCommentCommandValidator : AbstractValidator<AddLeadCommentCommand>
{
    public AddLeadCommentCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>Validates <see cref="UpdateLeadCommentCommand"/>.</summary>
public sealed class UpdateLeadCommentCommandValidator : AbstractValidator<UpdateLeadCommentCommand>
{
    public UpdateLeadCommentCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.CommentId).NotEmpty();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>Validates <see cref="AddLeadProductCommand"/>.</summary>
public sealed class AddLeadProductCommandValidator : AbstractValidator<AddLeadProductCommand>
{
    public AddLeadProductCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.Line).SetValidator(new LeadProductInputValidator());
    }
}

/// <summary>Validates <see cref="UpdateLeadProductCommand"/>.</summary>
public sealed class UpdateLeadProductCommandValidator : AbstractValidator<UpdateLeadProductCommand>
{
    public UpdateLeadProductCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.LineId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Status).Must(v => Enum.IsDefined(typeof(LeadProductStatus), v)).WithMessage("Status must be 1 (Offered), 2 (Accepted) or 3 (Rejected).");
    }
}

/// <summary>Mapping of the lead work items.</summary>
internal static class LeadWorkMapping
{
    internal static LeadInteractionDto ToDto(LeadInteraction i) => new()
    {
        Id = i.Id, LeadId = i.LeadId, ContactTypeCompanyId = i.ContactTypeCompanyId, Direction = (int)i.Direction, OccurredAt = i.OccurredAt,
        DurationMinutes = i.DurationMinutes, UserId = i.UserId, Outcome = (int)i.Outcome, Summary = i.Summary, CreatedAt = i.Created
    };

    internal static LeadCommentDto ToDto(LeadComment c) => new()
    {
        Id = c.Id, LeadId = c.LeadId, LeadInteractionId = c.LeadInteractionId, Body = c.Body, UserId = c.UserId, CreatedAt = c.Created
    };

    internal static LeadProductDto ToDto(LeadProduct l, Product? product) => new()
    {
        Id = l.Id, LeadId = l.LeadId, ProductId = l.ProductId, ProductCode = product?.Code ?? string.Empty, ProductName = product?.Name ?? string.Empty,
        Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountPercent = l.DiscountPercent, LineTotal = l.LineTotal, Status = (int)l.Status,
        OfferedInInteractionId = l.OfferedInInteractionId, CreatedAt = l.Created
    };
}

/// <summary>Handles <see cref="AddLeadInteractionCommand"/>.</summary>
public sealed class AddLeadInteractionCommandHandler(
    IUnitOfWork unitOfWork,
    LeadCommandContext context,
    LeadReferenceValidator references,
    LeadWorkRules work)
    : IRequestHandler<AddLeadInteractionCommand, Response<LeadInteractionDto>>
{
    public async Task<Response<LeadInteractionDto>> Handle(AddLeadInteractionCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadInteractionDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadInteractionDto>(actor, request.LeadId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        if (LeadCommandContext.RequireOpen<LeadInteractionDto>(lead) is { } notOpen)
        {
            return notOpen;
        }

        if (await references.PersonAsync(actor.CompanyId, lead.PersonId) is null)
        {
            return Response<LeadInteractionDto>.Error(LeadErrorCodes.PersonInactive, ["The person of the lead is inactive."]);
        }

        var (interaction, buildError) = await work.BuildInteractionAsync<LeadInteractionDto>(lead, request.Interaction, actor.UserId);
        if (buildError is not null)
        {
            return buildError;
        }

        LeadWorkRules.Touch(lead, interaction!.OccurredAt);
        await unitOfWork.GetRepository<LeadInteraction>().InsertAsync(interaction);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadInteractionDto> { IsSuccess = true, Message = "Contact registered successfully.", Data = LeadWorkMapping.ToDto(interaction) };
    }
}

/// <summary>Handles <see cref="UpdateLeadInteractionCommand"/>.</summary>
public sealed class UpdateLeadInteractionCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context, LeadWorkRules work)
    : IRequestHandler<UpdateLeadInteractionCommand, Response<LeadInteractionDto>>
{
    public async Task<Response<LeadInteractionDto>> Handle(UpdateLeadInteractionCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadInteractionDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadInteractionDto>(actor, request.LeadId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        var repository = unitOfWork.GetRepository<LeadInteraction>();
        var interaction = await repository.GetAsync(request.InteractionId);
        if (interaction is null || interaction.LeadId != lead.Id || interaction.GcRecord != 0)
        {
            return Response<LeadInteractionDto>.Error(LeadErrorCodes.InteractionNotFound, ["Contact not found."]);
        }

        if (LeadCommandContext.RequireOpen<LeadInteractionDto>(lead) is { } notOpen)
        {
            return notOpen;
        }

        if (interaction.UserId != actor.UserId || interaction.Created < DateTime.UtcNow.AddHours(-24))
        {
            return LeadCommandContext.Forbidden<LeadInteractionDto>("Only its author can edit a contact, within 24 hours.");
        }

        var (rebuilt, buildError) = await work.BuildInteractionAsync<LeadInteractionDto>(lead, request.Interaction, actor.UserId);
        if (buildError is not null)
        {
            return buildError;
        }

        interaction.ContactTypeCompanyId = rebuilt!.ContactTypeCompanyId;
        interaction.Direction = rebuilt.Direction;
        interaction.OccurredAt = request.Interaction.OccurredAt?.ToUniversalTime() ?? interaction.OccurredAt;
        interaction.DurationMinutes = rebuilt.DurationMinutes;
        interaction.Outcome = rebuilt.Outcome;
        interaction.Summary = rebuilt.Summary;
        LeadWorkRules.Touch(lead, interaction.OccurredAt);

        await repository.UpdateAsync(interaction);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadInteractionDto> { IsSuccess = true, Message = "Contact updated successfully.", Data = LeadWorkMapping.ToDto(interaction) };
    }
}

/// <summary>Handles <see cref="AddLeadCommentCommand"/>.</summary>
public sealed class AddLeadCommentCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context)
    : IRequestHandler<AddLeadCommentCommand, Response<LeadCommentDto>>
{
    public async Task<Response<LeadCommentDto>> Handle(AddLeadCommentCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadCommentDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadCommentDto>(actor, request.LeadId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (request.LeadInteractionId is { } interactionId
            && !(await unitOfWork.GetRepository<LeadInteraction>().GetAllIncludingDeletedAsync(i => i.Id == interactionId && i.LeadId == request.LeadId && i.GcRecord == 0)).Any())
        {
            return Response<LeadCommentDto>.Error(LeadErrorCodes.InteractionNotFound, ["Contact not found."]);
        }

        var comment = new LeadComment
        {
            CompanyId = actor.CompanyId, LeadId = load!.Lead.Id, LeadInteractionId = request.LeadInteractionId, Body = request.Body.Trim(), UserId = actor.UserId
        };
        await unitOfWork.GetRepository<LeadComment>().InsertAsync(comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadCommentDto> { IsSuccess = true, Message = "Comment added successfully.", Data = LeadWorkMapping.ToDto(comment) };
    }
}

/// <summary>Handles <see cref="UpdateLeadCommentCommand"/>.</summary>
public sealed class UpdateLeadCommentCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context)
    : IRequestHandler<UpdateLeadCommentCommand, Response<LeadCommentDto>>
{
    public async Task<Response<LeadCommentDto>> Handle(UpdateLeadCommentCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadCommentDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (comment, error) = await LeadCommentAccess.LoadAsync<LeadCommentDto>(unitOfWork, context, actor, request.LeadId, request.CommentId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        comment!.Body = request.Body.Trim();
        await unitOfWork.GetRepository<LeadComment>().UpdateAsync(comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadCommentDto> { IsSuccess = true, Message = "Comment updated successfully.", Data = LeadWorkMapping.ToDto(comment) };
    }
}

/// <summary>Handles <see cref="DeleteLeadCommentCommand"/>.</summary>
public sealed class DeleteLeadCommentCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context)
    : IRequestHandler<DeleteLeadCommentCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteLeadCommentCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<Guid>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (comment, error) = await LeadCommentAccess.LoadAsync<Guid>(unitOfWork, context, actor, request.LeadId, request.CommentId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        comment!.MarkAsDeleted();
        await unitOfWork.GetRepository<LeadComment>().UpdateAsync(comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<Guid> { IsSuccess = true, Message = "Comment deleted successfully.", Data = comment.Id };
    }
}

/// <summary>A visible lead's live comment written by the caller (only the author edits or deletes it).</summary>
internal static class LeadCommentAccess
{
    internal static async Task<(LeadComment? Comment, Response<T>? Error)> LoadAsync<T>(
        IUnitOfWork unitOfWork, LeadCommandContext context, LeadActor actor, Guid leadId, Guid commentId, CancellationToken cancellationToken)
    {
        var (_, error) = await context.LoadAsync<T>(actor, leadId, cancellationToken);
        if (error is not null)
        {
            return (null, error);
        }

        var comment = await unitOfWork.GetRepository<LeadComment>().GetAsync(commentId);
        if (comment is null || comment.LeadId != leadId || comment.GcRecord != 0)
        {
            return (null, Response<T>.Error(LeadErrorCodes.CommentNotFound, ["Comment not found."]));
        }

        return comment.UserId == actor.UserId
            ? (comment, null)
            : (null, LeadCommandContext.Forbidden<T>("Only its author can edit or delete a comment."));
    }
}

/// <summary>Handles <see cref="AddLeadProductCommand"/>.</summary>
public sealed class AddLeadProductCommandHandler(
    IUnitOfWork unitOfWork,
    LeadCommandContext context,
    LeadWorkRules work,
    LeadValueCalculator values)
    : IRequestHandler<AddLeadProductCommand, Response<LeadProductDto>>
{
    public async Task<Response<LeadProductDto>> Handle(AddLeadProductCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadProductDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadProductDto>(actor, request.LeadId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        if (LeadCommandContext.RequireOpen<LeadProductDto>(lead) is { } notOpen)
        {
            return notOpen;
        }

        if (request.Line.OfferedInInteractionId is { } interactionId
            && !(await unitOfWork.GetRepository<LeadInteraction>().GetAllIncludingDeletedAsync(i => i.Id == interactionId && i.LeadId == lead.Id && i.GcRecord == 0)).Any())
        {
            return Response<LeadProductDto>.Error(LeadErrorCodes.InteractionNotFound, ["Contact not found."]);
        }

        // The default price uses today's rate, which the recalculation stores on the lead as well.
        var (rate, rateError) = await values.RateAsync<LeadProductDto>(lead.CompanyId, lead.CurrencyId, cancellationToken);
        if (rateError is not null)
        {
            return rateError;
        }

        lead.ExchangeRate = rate!.Rate;
        var (line, lineError) = await work.BuildLineAsync<LeadProductDto>(lead, request.Line);
        if (lineError is not null)
        {
            return lineError;
        }

        if (await values.RecalculateAsync<LeadProductDto>(lead, null, [line!], cancellationToken) is { } valueError)
        {
            return valueError;
        }

        await unitOfWork.GetRepository<LeadProduct>().InsertAsync(line!);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var product = await unitOfWork.GetRepository<Product>().GetAsync(line!.ProductId);
        return new Response<LeadProductDto> { IsSuccess = true, Message = "Product added successfully.", Data = LeadWorkMapping.ToDto(line, product) };
    }
}

/// <summary>Handles <see cref="UpdateLeadProductCommand"/>.</summary>
public sealed class UpdateLeadProductCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context, LeadValueCalculator values)
    : IRequestHandler<UpdateLeadProductCommand, Response<LeadProductDto>>
{
    public async Task<Response<LeadProductDto>> Handle(UpdateLeadProductCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadProductDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (lead, line, error) = await LeadLineAccess.LoadAsync<LeadProductDto>(unitOfWork, context, actor, request.LeadId, request.LineId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        line!.Quantity = request.Quantity;
        line.UnitPrice = request.UnitPrice;
        line.DiscountPercent = request.DiscountPercent;
        line.Status = (LeadProductStatus)request.Status;
        line.Recalculate();
        if (await values.RecalculateAsync<LeadProductDto>(lead!, null, [line], cancellationToken) is { } valueError)
        {
            return valueError;
        }

        await unitOfWork.GetRepository<LeadProduct>().UpdateAsync(line);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead!);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var product = await unitOfWork.GetRepository<Product>().GetAsync(line.ProductId);
        return new Response<LeadProductDto> { IsSuccess = true, Message = "Product line updated successfully.", Data = LeadWorkMapping.ToDto(line, product) };
    }
}

/// <summary>Handles <see cref="DeleteLeadProductCommand"/>.</summary>
public sealed class DeleteLeadProductCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context, LeadValueCalculator values)
    : IRequestHandler<DeleteLeadProductCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteLeadProductCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<Guid>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (lead, line, error) = await LeadLineAccess.LoadAsync<Guid>(unitOfWork, context, actor, request.LeadId, request.LineId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        line!.MarkAsDeleted();
        // With no lines left the value stays as it was until it is written by hand again.
        if (await values.RecalculateAsync<Guid>(lead!, lead!.EstimatedValue, [line], cancellationToken) is { } valueError)
        {
            return valueError;
        }

        await unitOfWork.GetRepository<LeadProduct>().UpdateAsync(line);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<Guid> { IsSuccess = true, Message = "Product line deleted successfully.", Data = line.Id };
    }
}

/// <summary>A live line of an open, visible lead.</summary>
internal static class LeadLineAccess
{
    internal static async Task<(Lead? Lead, LeadProduct? Line, Response<T>? Error)> LoadAsync<T>(
        IUnitOfWork unitOfWork, LeadCommandContext context, LeadActor actor, Guid leadId, Guid lineId, CancellationToken cancellationToken)
    {
        var (load, error) = await context.LoadAsync<T>(actor, leadId, cancellationToken);
        if (error is not null)
        {
            return (null, null, error);
        }

        var line = await unitOfWork.GetRepository<LeadProduct>().GetAsync(lineId);
        if (line is null || line.LeadId != leadId || line.GcRecord != 0)
        {
            return (null, null, Response<T>.Error(LeadErrorCodes.ProductLineNotFound, ["Product line not found."]));
        }

        return LeadCommandContext.RequireOpen<T>(load!.Lead) is { } notOpen ? (null, null, notOpen) : (load.Lead, line, null);
    }
}
