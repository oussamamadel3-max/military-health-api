using API.Infrastructure;
using Application.Dtos.CoreDtos;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.Core;

[ApiController, Route("api/core/org-unit-types"), Authorize]
public class OrgUnitTypesController : ControllerBase
{
    private readonly IOrgUnitTypeService _service;
    public OrgUnitTypesController(IOrgUnitTypeService service) => _service = service;

    [HttpGet, AuthorizePermission("OrgStructure.Read")]
    public async Task<IResult> GetList([FromQuery, Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery, Range(1, 100)] int pageSize = 10, [FromQuery] string? q = null, [FromQuery] bool? isActive = null) =>
        ApiResponse.Success("تم جلب أنواع الوحدات التنظيمية بنجاح.", await _service.GetListAsync(pageNumber, pageSize, q, isActive));

    [HttpGet("active"), AuthorizePermission("OrgStructure.Read")]
    public async Task<IResult> GetActive() => ApiResponse.Success("تم جلب أنواع الوحدات التنظيمية النشطة.", await _service.GetAllActiveAsync());

    [HttpGet("{id:guid}"), AuthorizePermission("OrgStructure.Read")]
    public async Task<IResult> GetById(Guid id) => ApiResponse.Success("تم جلب نوع الوحدة التنظيمية بنجاح.", await _service.GetByIdAsync(id));

    [HttpPost, AuthorizePermission("OrgStructure.Create")]
    public async Task<IResult> Create(CreateOrgUnitTypeDto dto) => ApiResponse.Success("تم إنشاء نوع الوحدة التنظيمية بنجاح.", await _service.CreateAsync(dto));

    [HttpPut("{id:guid}"), AuthorizePermission("OrgStructure.Edit")]
    public async Task<IResult> Update(Guid id, UpdateOrgUnitTypeDto dto)
    {
        await _service.UpdateAsync(id, dto);
        return ApiResponse.Success("تم تحديث نوع الوحدة التنظيمية بنجاح.");
    }

    [HttpPatch("{id:guid}/active"), AuthorizePermission("OrgStructure.Delete")]
    public async Task<IResult> SetActive(Guid id, SetActiveStatusDto dto)
    {
        await _service.SetActiveAsync(id, dto.IsActive);
        return ApiResponse.Success("تم تحديث حالة نوع الوحدة التنظيمية بنجاح.");
    }
}
