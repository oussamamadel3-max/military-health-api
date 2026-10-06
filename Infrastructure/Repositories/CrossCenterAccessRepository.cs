using Application.Interfaces.Repositories;
using Domain.Entities.Audit;
using Infrastructure.Persistence;

namespace Infrastructure.Repositories;

public class CrossCenterAccessRepository : ICrossCenterAccessRepository
{
    private readonly AppDbContext _context;

    public CrossCenterAccessRepository(AppDbContext context) => _context = context;

    public void Add(CrossCenterAccess access) => _context.CrossCenterAccesses.Add(access);
}
