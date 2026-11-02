using System.Security.Cryptography;
using FluentValidation;
using Meridian.Api.Shared.Auth;
using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Jobs;
using Meridian.Api.Shared.Observability;
using Meridian.Api.Shared.Storage;

namespace Meridian.Api.Features.Imports;

public static class UploadImport
{
    public class Request
    {
        public required IFormFile? File { get; init; }
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.File)
                .Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("Choose a CSV file to upload.")
                .Must(f => f!.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                .WithMessage("The file must have the .csv extension.")
                .Must(f => f!.Length > 0).WithMessage("The file is empty.");
        }
    }

    public class Handler(
        AppDbContext db,
        IFileStore files,
        JobQueue jobs,
        ICurrentUser currentUser,
        ICorrelationContext correlation,
        IValidator<Request> validator)
    {
        public async Task<ImportCreatedDto> Handle(Request request, CancellationToken ct)
        {
            await validator.ValidateAndThrowAsync(request, ct);
            IFormFile file = request.File!;

            Guid id = Guid.NewGuid();
            string blobKey = $"imports/{id}.csv";

            // Blob first: if it fails no import exists, and a blob with no import is harmless
            using SHA256 sha256 = SHA256.Create();
            await using (CryptoStream hashing = new(file.OpenReadStream(), sha256, CryptoStreamMode.Read))
            {
                await files.SaveAsync(blobKey, hashing, ct);
            }

            db.Imports.Add(new Import
            {
                Id = id,
                FileName = file.FileName,
                BlobKey = blobKey,
                Sha256 = Convert.ToHexStringLower(sha256.Hash!),
                SizeBytes = file.Length,
                UploadedBy = currentUser.Id,
                Status = ImportStatus.Pending,
                Phase = ImportPhase.Queued,
                CorrelationId = correlation.Id,
            });
            jobs.Enqueue(ImportJobPayload.JobType, new ImportJobPayload { ImportId = id, CorrelationId = correlation.Id });
            await db.SaveChangesAsync(ct);

            return new ImportCreatedDto { ImportId = id };
        }
    }
}
