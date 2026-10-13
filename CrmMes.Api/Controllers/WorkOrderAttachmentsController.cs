using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Foto legate a un lotto/commessa specifico: controllo qualità (lo stato reale di questo lotto)
/// o istruzioni operatore pensate apposta per questo lotto — non il prodotto in generale, per quello
/// esistono già i documenti tecnici (TechnicalDocumentsController). Aperto a chiunque sia autenticato,
/// come il resto del lavoro di reparto (segnalare un fermo o una non conformità non è riservato a un ruolo).</summary>
[ApiController]
[Authorize]
[Route("api/work-orders/{workOrderId:guid}/attachments")]
public class WorkOrderAttachmentsController(ApplicationDbContext db, IFileStorage files) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WorkOrderAttachmentResponse>>> GetAttachments(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var attachments = await db.WorkOrderAttachments.AsNoTracking()
            .Where(a => a.WorkOrderId == workOrderId)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => new WorkOrderAttachmentResponse(a.Id, a.Kind, a.Caption, a.UploadedBy, a.UploadedAt))
            .ToListAsync(cancellationToken);
        return Ok(attachments);
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> GetContent(Guid workOrderId, Guid id, CancellationToken cancellationToken = default)
    {
        var fileId = await db.WorkOrderAttachments.AsNoTracking()
            .Where(a => a.WorkOrderId == workOrderId && a.Id == id)
            .Select(a => a.FileId)
            .FirstOrDefaultAsync(cancellationToken);
        if (fileId == Guid.Empty)
        {
            return NotFound();
        }

        var content = await files.GetAsync(fileId, cancellationToken);
        return content is null ? NotFound() : File(content.Content, content.ContentType);
    }

    [HttpPost]
    [RequestSizeLimit(ImageUploadRules.MaxSizeBytes)]
    public async Task<ActionResult<WorkOrderAttachmentResponse>> Upload(
        Guid workOrderId, IFormFile file, [FromForm] string kind, [FromForm] string? caption, CancellationToken cancellationToken = default)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, cancellationToken))
        {
            return NotFound(new { message = "Commessa non trovata." });
        }

        if (!WorkOrderAttachmentKinds.IsKnown(kind))
        {
            return BadRequest(new { message = $"Tipo non valido. Valori ammessi: {string.Join(", ", WorkOrderAttachmentKinds.Labels.Keys)}." });
        }

        var error = ImageUploadRules.Validate(file?.ContentType, (int)(file?.Length ?? 0));
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        using var stream = new MemoryStream();
        await file!.CopyToAsync(stream, cancellationToken);
        var uploadedBy = User.FindFirstValue(ClaimTypes.Name);
        var fileId = await files.SaveAsync(stream.ToArray(), file.ContentType, uploadedBy, cancellationToken);

        var attachment = new WorkOrderAttachment
        {
            WorkOrderId = workOrderId,
            Kind = kind,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            FileId = fileId,
            UploadedBy = uploadedBy,
        };
        db.WorkOrderAttachments.Add(attachment);
        AuditTrail.Add(db, User, "WorkOrderAttachmentAdded", "WorkOrder", workOrderId,
            $"Foto ({WorkOrderAttachmentKinds.Labels[kind]}) aggiunta al lotto.");
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAttachments), new { workOrderId },
            new WorkOrderAttachmentResponse(attachment.Id, attachment.Kind, attachment.Caption, attachment.UploadedBy, attachment.UploadedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid workOrderId, Guid id, CancellationToken cancellationToken = default)
    {
        var attachment = await db.WorkOrderAttachments.FirstOrDefaultAsync(a => a.WorkOrderId == workOrderId && a.Id == id, cancellationToken);
        if (attachment is null)
        {
            return NotFound();
        }

        var fileId = attachment.FileId;
        db.WorkOrderAttachments.Remove(attachment);
        AuditTrail.Add(db, User, "WorkOrderAttachmentRemoved", "WorkOrder", workOrderId, "Foto rimossa dal lotto.");
        await db.SaveChangesAsync(cancellationToken);
        await files.DeleteAsync(fileId, cancellationToken);
        return NoContent();
    }
}

public sealed record WorkOrderAttachmentResponse(Guid Id, string Kind, string? Caption, string? UploadedBy, DateTime UploadedAt);
