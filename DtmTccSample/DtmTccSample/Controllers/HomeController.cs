using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dtmcli;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace DtmTccSample.Controllers
{
    [ApiController]
    [Route("/api/[controller]/[action]")]
    public class HomeController : ControllerBase
    {
        TccGlobalTransaction globalTransaction;
        ILogger logger;

        public HomeController(TccGlobalTransaction transaction, ILoggerFactory loggerFactory)
        {
            globalTransaction = transaction;
            logger = loggerFactory.CreateLogger<HomeController>();
        }

        [HttpPost]
        public async Task<RestResult> Demo()
        {
            //子事务url
            var svc = "http://192.168.1.210:5000/api";
            TransRequest request = new TransRequest() { Amount = 30 };
            var cts = new CancellationTokenSource();
            try
            {
                var transOutTryReturn = string.Empty;
                var transInTryReturn = string.Empty;
 
                //当调用globalTransaction.Config设置了waitResult=true时，会返回子事务cancel或confirm阶段执行结果
                //否则Excecute不会等子事务响应结束，即刻返回
                //globalTransaction.Config不调用，会使用dtm的默认值
                var dtmResult = await globalTransaction
                    .Config(true, timeoutToFail: 60)
                    .Excecute(async (tcc) =>
                {
                    transOutTryReturn = await tcc.CallBranch(request, svc + "/TransOut/Try", svc + "/TransOut/Confirm", svc + "/TransOut/Cancel", cts.Token);
                    //抛出异常：目的是让sdk客户端捕获异常，通知dtm服务端，try阶段遇到了异常，所有子事务回滚
                    if (!transOutTryReturn.Contains("SUCCESS"))
                        throw new AccessViolationException($"{transOutTryReturn}");

                    transInTryReturn = await tcc.CallBranch(request, svc + "/TransIn/Try", svc + "/TransIn/Confirm", svc + "/TransIn/Cancel", cts.Token);
                    //抛出异常：目的是让sdk客户端捕获异常，通知dtm服务端，try阶段遇到了异常，所有子事务回滚
                    if (!transInTryReturn.Contains("SUCCESS"))
                        throw new AccessViolationException($"{transInTryReturn}");
                }, cts.Token);
                if(!transOutTryReturn.Contains("SUCCESS")|| !transInTryReturn.Contains("SUCCESS"))
                    logger.LogError($"try阶段异常:{transOutTryReturn}{transInTryReturn}");
                if (!dtmResult.Success)
                {
                    logger.LogError($"confirm或cancel阶段异常:{transOutTryReturn}{transInTryReturn}");
                    //dtmResult.Message可以包含错误信息，可以解析出业务异常，返回调用端
                    return new RestResult() { Result = $"{dtmResult.Message}"};
                }
                return new RestResult() { Result = "SUCCESS" };
            }
            catch (Exception ex)
            {
                return new RestResult() { Result = $"{ex.Message}" };
            }
        }
    }
}
