using Microsoft.AspNetCore.Mvc;

namespace FinSight.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
