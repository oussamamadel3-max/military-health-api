using API.Infrastructure;
using Application.Dtos.CoreDtos;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.Core;

[ApiController, Route("api/core/org-units"), Authorize]
public class OrgUnitsController : ControllerBase
{
    private readonly IOrgUnitService _service;
    public OrgUnitsController(IOrgUnitService service) => _service = service;

    [HttpGet, AuthorizePermission("OrgStructure.Read")]
    public async Task<IResult> GetList([FromQuery, Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery, Range(1, 100)] int pageSize = 10, [FromQuery] string? q = null, [FromQuery] string? status = null) =>
        ApiResponse.Success("تم جلب الوحدات التنظيمية بنجاح.", await _service.GetListAsync(pageNumber, pageSize, q, status));

    [HttpGet("dropdown"), AuthorizePermission("OrgStructure.Read")]
    public async Task<IResult> GetDropdown() => ApiResponse.Success("تم جلب قائمة الوحدات التنظيمية.", await _service.GetDropdownAsync());

    [HttpGet("tree"), AuthorizePermission("OrgStructure.Read")]
    public async Task<IResult> GetTree([FromQuery] string? status = null) =>
        ApiResponse.Success("تم جلب شجرة الوحدات التنظيمية.", await _service.GetTreeAsync(status));

    [HttpGet("{id:guid}"), AuthorizePermission("OrgStructure.Read")]
    public async Task<IResult> GetById(Guid id) => ApiResponse.Success("تم جلب الوحدة التنظيمية بنجاح.", await _service.GetByIdAsync(id));

    [HttpGet("{id:guid}/children"), AuthorizePermission("OrgStructure.Read")]
    public async Task<IResult> GetChildren(Guid id) => ApiResponse.Success("تم جلب الوحدات الفرعية.", await _service.GetChildrenAsync(id));

    [HttpGet("{id:guid}/descendants"), AuthorizePermission("OrgStructure.Read")]
    public async Task<IResult> GetDescendants(Guid id) => ApiResponse.Success("تم جلب الوحدات التابعة.", await _service.GetDescendantsAsync(id));

    [HttpPost, AuthorizePermission("OrgStructure.Create")]
    public async Task<IResult> Create(CreateOrgUnitDto dto) => ApiResponse.Success("تم إنشاء الوحدة التنظيمية بنجاح.", await _service.CreateAsync(dto));

    [HttpPut("{id:guid}"), AuthorizePermission("OrgStructure.Edit")]
    public async Task<IResult> Update(Guid id, UpdateOrgUnitDto dto)
    {
        await _service.UpdateAsync(id, dto);
        return ApiResponse.Success("تم تحديث الوحدة التنظيمية بنجاح.");
    }

    [HttpPatch("{id:guid}/status"), AuthorizePermission("OrgStructure.Delete")]
    public async Task<IResult> SetStatus(Guid id, SetRecordStatusDto dto)
    {
        await _service.SetStatusAsync(id, dto.Status);
        return ApiResponse.Success("تم تحديث حالة الوحدة التنظيمية بنجاح.");
    }

    [HttpDelete("{id:guid}"), AuthorizePermission("OrgStructure.Delete")]
    public async Task<IResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return ApiResponse.Success("تم حذف الوحدة التنظيمية بنجاح.");
    }
}
