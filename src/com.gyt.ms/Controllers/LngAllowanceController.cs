﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Castle.Core.Internal;
using Zer.AppServices;
using Zer.Entities;
using Zer.Framework.Attributes;
using Zer.Framework.Export;
using Zer.Framework.Export.Attributes;
using Zer.Framework.Import;
using Zer.Framework.Mvc.Logs.Attributes;
using Zer.GytDto;
using Zer.GytDto.SearchFilters;

namespace com.gyt.ms.Controllers
{
    public class LngAllowanceController : BaseController
    {
        private readonly ILngAllowanceService _lngAllowanceService;
        private readonly ICompanyService _companyService;
        private readonly ITruckInfoService _truckInfoService;

        public LngAllowanceController(
            ILngAllowanceService lngAllowanceService,
            ICompanyService companyService,
            ITruckInfoService truckInfoService)
        {
            _lngAllowanceService = lngAllowanceService;
            _companyService = companyService;
            _truckInfoService = truckInfoService;
        }

        // GET: LngAllowance
        [UserActionLog("LNG补贴信息首页",ActionType.查询)]
        public ActionResult Index(LngAllowanceSearchDto filter = null)
        {
            ViewBag.Filter = filter;

            var result = _lngAllowanceService.GetList(filter);
            return View(result);
        }

        public ActionResult Add()
        {
            ViewBag.CompanyList = _companyService.GetAll();
            return View();
        }

        [UserActionLog("LNG补贴信息导出", ActionType.查询)]
        public FileResult Export(LngAllowanceSearchDto searchDto)
        {
            if (searchDto == null) return null;

            searchDto.PageSize = Int32.MaxValue;
            searchDto.PageIndex = 1;
            var exportList = _lngAllowanceService.GetList(searchDto);

            return exportList == null ? null : ExportCsv(exportList.GetBuffer(), string.Format("LNG补贴信息{0:yyyyMMddhhmmssfff}", DateTime.Now));
        }


        //Todo: 建议优化检查检查重复业务逻辑
        [HttpPost]
        [UserActionLog("LNG补贴信息批量导入", ActionType.新增)]
        public ActionResult ImportFile(HttpPostedFileBase file)
        {
            if (file == null || file.InputStream == null) throw new Exception("文件上传失败，导入失败");

            var excelImport = new ExcelImport<LngAllowanceInfoDto>(file.InputStream);
            var lngAllowanceInfoDtoList = excelImport.Read();

            if (lngAllowanceInfoDtoList.IsNullOrEmpty()) throw new Exception("没有从文件中读取到任何数据，导入失败，请重试!");

            var sessionCode = AppendObjectToSession(lngAllowanceInfoDtoList);

            return RedirectToAction("SaveLngAllowanceData", "LngAllowance",
                new { id = sessionCode });
        }

        [ReplaceSpecialCharInParameter("-", "_")]
        [GetParameteFromSession("id")]
        [UnLog]
        public ActionResult SaveLngAllowanceData(string id)
        {
            var lngAllowanceInfoDtoList = GetValueFromSession<List<LngAllowanceInfoDto>>(id);

            // 检测数据库中已经存在的重复数据
            var existsLngAllowanceInfoDtoList = lngAllowanceInfoDtoList
                .Where(x => _lngAllowanceService.Exists(x))
                .ToList();

            // 筛选出需要导入的数据
            var mustImportLngAllowanceInfoDtoList =
                lngAllowanceInfoDtoList
                    .Where(x => !existsLngAllowanceInfoDtoList.Select(lng => lng.TruckNo).Contains(x.TruckNo)).ToList();

            // 初始化检测并注册其中的新公司信息
            var companyInfoDtoList = InitCompanyInfoDtoList(mustImportLngAllowanceInfoDtoList);

            var dic = mustImportLngAllowanceInfoDtoList.ToDictionary(x => x.TruckNo, v => v.CompanyId);

            // 初始化检测并注册其中的新车辆信息
            InitTruckInfoDtoList(dic, companyInfoDtoList);

            // 保存LNG补贴信息，并得到保存成功的结果
            var importSuccessList = _lngAllowanceService.AddRange(mustImportLngAllowanceInfoDtoList);

            var importFailedList = mustImportLngAllowanceInfoDtoList.Where(x => importSuccessList.Contains(x))
                .ToList();

            // 展示导入结果
            ViewBag.SuccessCode = AppendObjectToSession(importSuccessList);
            ViewBag.FailedCode = AppendObjectToSession(importFailedList);
            ViewBag.ExistedCode = AppendObjectToSession(existsLngAllowanceInfoDtoList);

            ViewBag.SuccessList = importSuccessList;
            ViewBag.FailedList = importFailedList;
            ViewBag.ExistedList = existsLngAllowanceInfoDtoList;
            return View("ImportResult");
        }

        [HttpPost]
        [ReplaceSpecialCharInParameter("-", "_")]
        [UserActionLog("LNG补贴信息单条新增", ActionType.新增)]
        public JsonResult AddPost(LngAllowanceInfoDto dto)
        {
            CompanyInfoDto companyInfoDto = _companyService.GetById(dto.CompanyId);
            dto.CompanyName = companyInfoDto.CompanyName;

            if (_truckInfoService.Exists(dto.TruckNo))
            {
                _truckInfoService.GetByTruckNo(dto.TruckNo);
            }
            else
            {
                _truckInfoService.Add(new TruckInfoDto()
                {
                    CompanyId = companyInfoDto.Id,
                    CompanyName = companyInfoDto.CompanyName,
                    FrontTruckNo = dto.TruckNo
                });
            }

            _lngAllowanceService.Add(dto);

            return Success();
        }

        [HttpPost]
        [UserActionLog("LNG补贴信息补贴状态更改", ActionType.更改状态)]
        public JsonResult ChangStatus(int infoId)
        {
            var infoDto = _lngAllowanceService.GetById(infoId);
            if (infoDto.Status==LngStatus.已补贴)
            {
                return Fail("这条记录已是补贴状态，请核实！");
            }

            infoDto = _lngAllowanceService.ChangStatus(infoId);
            return infoDto.Status!=LngStatus.已补贴 ? Fail("失败，请联系系统管理人员！") : Success("修改补贴状态成功！");
        }

        private List<CompanyInfoDto> InitCompanyInfoDtoList(List<LngAllowanceInfoDto> lngAllowanceInfoDtoList)
        {
            //var companyNameList = lngAllowanceInfoDtoList.Select(x => x.CompanyName).ToList();
            var improtCompanyInfoDtoList = new List<CompanyInfoDto>();
            foreach (var lngAllowanceInfoDto in lngAllowanceInfoDtoList)
            {
                improtCompanyInfoDtoList.Add(
                    new CompanyInfoDto
                    {
                        CompanyName = lngAllowanceInfoDto.CompanyName
                    }
                );
            }

            // 注册新增公司信息
            var companyInfoDtoList = _companyService.QueryAfterValidateAndRegist(improtCompanyInfoDtoList);

            foreach (var lngAllowanceInfoDto in lngAllowanceInfoDtoList)
            {
                var currentComapnyInfoDto =
                    companyInfoDtoList.Single(x => x.CompanyName == lngAllowanceInfoDto.CompanyName);

                lngAllowanceInfoDto.CompanyId = currentComapnyInfoDto.Id;
            }
            return companyInfoDtoList;
        }

        private void InitTruckInfoDtoList(Dictionary<string, int> dic, List<CompanyInfoDto> companyInfoDtoList)
        {
            var waitForValidateTruckList = new List<TruckInfoDto>();

            foreach (var truckNo in dic.Keys)
            {
                var companyInfo = companyInfoDtoList.Single(x => x.Id == dic[truckNo]);
                var truckDto = new TruckInfoDto()
                {
                    CompanyName = companyInfo.CompanyName,
                    CompanyId = companyInfo.Id,
                    FrontTruckNo = truckNo
                };

                waitForValidateTruckList.Add(truckDto);
            }

            // 注册新增车辆信息
            _truckInfoService.QueryAfterValidateAndRegist(waitForValidateTruckList);
        }

    }
}