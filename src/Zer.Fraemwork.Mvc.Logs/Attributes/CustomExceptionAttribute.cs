using System;
using System.Web.Mvc;
using System.Web.Routing;
using Zer.Entities;
using Zer.Framework.Dependency;
using Zer.Framework.Extensions;
using Zer.GytDataService;

namespace Zer.Framework.Mvc.Logs.Attributes
{
    public class CustomExceptionAttribute : FilterAttribute, IExceptionFilter
    {
        private static readonly ISystemLogInfoDataService Logger;
        private readonly string _viewName;

        static CustomExceptionAttribute()
        {
            Logger = IocManager.Instance.Resolve<ISystemLogInfoDataService>();
        }

        public CustomExceptionAttribute(string viewName)
        {
            _viewName = viewName;
        }

        public void OnException(ExceptionContext filterContext)
        {
            var exceptionMessage = filterContext.Exception.Message;
            var controllerName = (string)filterContext.RouteData.Values["controller"];
            var actionName = (string)filterContext.RouteData.Values["action"];
            
            var systemLog = new SystemLogInfo();
            systemLog.ActionName = actionName;
            systemLog.ControllerName = controllerName;
            systemLog.Content = exceptionMessage;

            Logger.Insert(systemLog);

            var view = new ViewResult();
            view.ViewBag.Exception = filterContext.Exception;
            view.ViewName = _viewName;

            filterContext.Result = view;
            filterContext.ExceptionHandled = true;
        }
    }
}