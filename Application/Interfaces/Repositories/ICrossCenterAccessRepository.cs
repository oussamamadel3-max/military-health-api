using Domain.Entities.Audit;

namespace Application.Interfaces.Repositories;

public interface ICrossCenterAccessRepository
{
    void Add(CrossCenterAccess access);
}
