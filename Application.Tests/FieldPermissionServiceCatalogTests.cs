using Application.Common.Permissions;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Domain.Enums;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Application.Tests;

public class FieldPermissionServiceCatalogTests
{
    [Fact]
    public async Task CreateAsync_rejects_entity_outside_catalog_even_when_catalog_empty()
    {
        var catalog = new Mock<IFieldPermissionCatalog>();
        catalog.Setup(c => c.IsEntityRegistered("Member")).Returns(false);

        var gate = new Mock<IPermissionGate>();
        var service = CreateService(catalog.Object, gate.Object);

        var error = await Assert.ThrowsAsync<InvalidRoleDataException>(() =>
            service.CreateAsync(new CreateFieldPermissionDto
            {
                EntityName = "Member",
                FieldNames = ["FullName"],
                AccessType = FieldAccessType.Read
            }));

        Assert.Contains("غير معتمد في كتالوج", error.Message, StringComparison.Ordinal);
        gate.Verify(g => g.EnsureAll(SystemPermissions.FieldPermissionManagement.Create), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_rejects_field_outside_catalog()
    {
        var catalog = new Mock<IFieldPermissionCatalog>();
        catalog.Setup(c => c.IsEntityRegistered("Probe")).Returns(true);
        catalog.Setup(c => c.IsFieldRegistered("Probe", "Allowed")).Returns(true);
        catalog.Setup(c => c.IsFieldRegistered("Probe", "Forbidden")).Returns(false);

        var service = CreateService(catalog.Object, Mock.Of<IPermissionGate>());

        var error = await Assert.ThrowsAsync<InvalidRoleDataException>(() =>
            service.CreateAsync(new CreateFieldPermissionDto
            {
                EntityName = "Probe",
                FieldNames = ["Allowed", "Forbidden"],
                AccessType = FieldAccessType.Read
            }));

        Assert.Contains("Forbidden", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEntitiesAsync_returns_catalog_entities()
    {
        var catalog = new Mock<IFieldPermissionCatalog>();
        catalog.Setup(c => c.GetEntities()).Returns(
        [
            new EntityInfoDto { Name = "Probe", DisplayName = "تجريبي" }
        ]);

        var service = CreateService(catalog.Object, Mock.Of<IPermissionGate>());
        var entities = await service.GetEntitiesAsync();

        Assert.Single(entities);
        Assert.Equal("Probe", entities[0].Name);
    }

    private static FieldPermissionService CreateService(
        IFieldPermissionCatalog catalog,
        IPermissionGate gate)
    {
        return new FieldPermissionService(
            Mock.Of<IFieldPermissionRepository>(),
            Mock.Of<IUserRepository>(),
            Mock.Of<IRoleRepository>(),
            Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IUnitOfWork>(),
            CreateUserManager(),
            Mock.Of<IMapper>(),
            gate,
            catalog);
    }

    private static UserManager<User> CreateUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new UserManager<User>(
            store.Object,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<User>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<User>>>());
    }
}
