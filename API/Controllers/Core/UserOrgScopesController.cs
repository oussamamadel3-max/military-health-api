using API.Infrastructure;
using Application.Dtos.CoreDtos;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.Core;

[ApiController, Route("api/core/user-org-scopes"), Authorize]
public class UserOrgScopesController : ControllerBase
{
    private readonly IUserOrgScopeService _service;
    public UserOrgScopesController(IUserOrgScopeService service) => _service = service;

    [HttpGet, AuthorizePermission("UserOrgScope.Read")]
    public async Task<IResult> GetList([FromQuery, Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery, Range(1, 100)] int pageSize = 10, [FromQuery] string? q = null, [FromQuery] string? status = null) =>
        ApiResponse.Success("تم جلب نطاقات المستخدمين التنظيمية.", await _service.GetListAsync(pageNumber, pageSize, q, status));

    [HttpGet("{id:guid}"), AuthorizePermission("UserOrgScope.Read")]
    public async Task<IResult> GetById(Guid id) => ApiResponse.Success("تم جلب نطاق المستخدم التنظيمي.", await _service.GetByIdAsync(id));

    [HttpGet("by-user/{userId}"), AuthorizePermission("UserOrgScope.Read")]
    public async Task<IResult> GetByUser(string userId) => ApiResponse.Success("تم جلب نطاقات المستخدم التنظيمية.", await _service.GetByUserIdAsync(userId));

    [HttpPost, AuthorizePermission("UserOrgScope.Create")]
    public async Task<IResult> Create(CreateUserOrgScopeDto dto) => ApiResponse.Success("تم إنشاء نطاق المستخدم التنظيمي بنجاح.", await _service.CreateAsync(dto));

    [HttpPut("{id:guid}"), AuthorizePermission("UserOrgScope.Edit")]
    public async Task<IResult> Update(Guid id, UpdateUserOrgScopeDto dto)
    {
        await _service.UpdateAsync(id, dto);
        return ApiResponse.Success("تم تحديث نطاق المستخدم التنظيمي بنجاح.");
    }

    [HttpDelete("{id:guid}"), AuthorizePermission("UserOrgScope.Delete")]
    public async Task<IResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return ApiResponse.Success("تم حذف نطاق المستخدم التنظيمي بنجاح.");
    }
}
