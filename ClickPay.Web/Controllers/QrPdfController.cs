using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ClickPay.Wallet.Core.Utility;
using ClickPay.Wallet.Core.Services;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace ClickPay.Web.Controllers
{
 [ApiController]
 [Route("api/[controller]")]
 public class QrPdfController : ControllerBase
 {
 private readonly ILocalSecureStore _secureStore;
 private readonly IConfiguration _configuration;

 public QrPdfController(ILocalSecureStore secureStore, IConfiguration configuration)
 {
 _secureStore = secureStore;
 _configuration = configuration;
 }

 [HttpGet]
 [Produces("application/pdf")]
 public async Task<IActionResult> Get()
 {
 // Resolve application name dynamically to support white-labeling.
 var appName = _configuration["AppName"]
 ?? System.AppContext.GetData("APP_NAME") as string
 ?? Assembly.GetEntryAssembly()?.GetName().Name
 ?? "App";

 // If the resolved name contains a dot (e.g. "ClickPay.Web"), keep only the part left of the first dot.
 if (!string.IsNullOrEmpty(appName))
 {
 var dotIndex = appName.IndexOf('.');
 if (dotIndex >0)
 {
 appName = appName.Substring(0, dotIndex).Trim();
 }
 }

 var pdfBytes = await QrPdfGenerator.GeneratePersonalQrPdfAsync(appName, _secureStore);
 return File(pdfBytes, "application/pdf", "qr_personale.pdf");
 }
 }
}
