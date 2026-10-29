using Application.Interfaces.Repositories;
using Domain.Entities.Medical;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>استعلامات وكتابة سجلات الاستشارات التخصصية عبر EF.</summary>
public class ConsultationRepository : IConsultationRepository
{
    private readonly AppDbContext _context;

    public ConsultationRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public IQueryable<Consultation> Query(bool asNoTracking = true)
        => asNoTracking ? _context.Consultations.AsNoTracking() : _context.Consultations;

    /// <inheritdoc />
    public Task<Consultation?> GetByIdInCenterAsync(
        Guid consultationId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = asNoTracking ? _context.Consultations.AsNoTracking() : _context.Consultations;
        return query.FirstOrDefaultAsync(
            c => c.ConsultationId == consultationId && c.CenterId == centerId,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Consultation consultation) => _context.Consultations.Add(consultation);
}